using System.Diagnostics.Metrics;
using Acheve.Common.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Acheve.Tools.ChaosMonkey.Tests
{
    public class StrategyRunnerTests
    {
        private static readonly int RoundSize = ChaosTargets.Scenarios.Count + ResilienceTargets.Names.Count;

        private static readonly TimeSpan Long = TimeSpan.FromMinutes(10);

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static (StrategyRunner Runner, IMeterFactory MeterFactory) CreateRunner(
            FakeChaosClient client,
            Dictionary<string, StrategyOptions> strategies,
            TimeProvider? time = null)
        {
            var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
            var runner = new StrategyRunner(
                client,
                new ChaosMonkeyMetrics(meterFactory),
                Options.Create(new ChaosMonkeyOptions { Strategies = strategies }),
                time ?? new FakeTimeProvider(),
                new Random(1),
                NullLogger<StrategyRunner>.Instance);

            return (runner, meterFactory);
        }

        // Step 1 breaks the image analysis and turns its caller's resilience off; step 2 breaks the Api.
        private static StrategyOptions TwoSteps(TimeSpan duration, bool repeat = false) => new()
        {
            Repeat = repeat,
            Steps =
            [
                new()
                {
                    Name = "One",
                    Duration = duration,
                    Chaos = { [Constants.Resources.ExternalImageProcess] = Constants.Chaos.LostCallbacks },
                    Resilience = { [Constants.Resources.ExternalImageProcessor] = Constants.Resilience.None }
                },
                new() { Name = "Two", Duration = duration, Chaos = { [Constants.Resources.Api] = Constants.Chaos.Flaky } }
            ]
        };

        private static StrategyOptions OneStep(string target, string scenario) => new()
        {
            Steps = [new() { Duration = Long, Chaos = { [target] = scenario } }]
        };

        // One switch of every target and caller, as target -> value.
        private static Dictionary<string, string> Round(FakeChaosClient client, int round) =>
            client.Calls.Skip(round * RoundSize).Take(RoundSize).ToDictionary(c => c.Target, c => c.Value);

        private static bool IsReset(Dictionary<string, string> round) =>
            round.Count == RoundSize &&
            ChaosTargets.Names.All(t => round[t] == Constants.Chaos.Healthy) &&
            ResilienceTargets.Names.All(c => round[c] == Constants.Resilience.Standard);

        private static async Task WaitForRounds(FakeChaosClient client, int rounds)
        {
            while (client.Calls.Count < rounds * RoundSize)
            {
                await Task.Delay(5, Cancellation);
            }
        }

        [Fact]
        public async Task Runs_each_step_switching_chaos_and_resilience_then_resets()
        {
            var client = new FakeChaosClient();
            var (runner, meterFactory) = CreateRunner(client, new() { ["test"] = TwoSteps(TimeSpan.FromMilliseconds(20)) }, TimeProvider.System);
            using var switches = new MetricCollector<long>(meterFactory, Constants.Telemetry.ChaosMonkeyMeterName, "chaos.monkey.switches");

            (await runner.RunAsync("test", Cancellation)).ShouldBeTrue();
            await runner.Completion;

            client.Calls.Count.ShouldBe(3 * RoundSize);

            var first = Round(client, 0);
            first[Constants.Resources.ExternalImageProcess].ShouldBe(Constants.Chaos.LostCallbacks);
            first[Constants.Resources.ExternalImageProcessor].ShouldBe(Constants.Resilience.None);
            ChaosTargets.Names.Where(t => t != Constants.Resources.ExternalImageProcess).ShouldAllBe(t => first[t] == Constants.Chaos.Healthy);
            ResilienceTargets.Names.Where(c => c != Constants.Resources.ExternalImageProcessor).ShouldAllBe(c => first[c] == Constants.Resilience.Standard);

            var second = Round(client, 1);
            second[Constants.Resources.Api].ShouldBe(Constants.Chaos.Flaky);
            second[Constants.Resources.ExternalImageProcessor].ShouldBe(Constants.Resilience.Standard);

            IsReset(Round(client, 2)).ShouldBeTrue();

            var measurements = switches.GetMeasurementSnapshot();
            measurements.Count.ShouldBe(client.Calls.Count);
            measurements.ShouldContain(m => (string?)m.Tags["chaos.kind"] == FakeChaosClient.Resilience);
        }

        [Fact]
        public async Task An_unknown_strategy_is_refused()
        {
            var client = new FakeChaosClient();
            var (runner, _) = CreateRunner(client, new() { ["test"] = TwoSteps(Long) });

            (await runner.RunAsync("nope", Cancellation)).ShouldBeFalse();

            client.Calls.ShouldBeEmpty();
            runner.Status.Strategy.ShouldBeNull();
        }

        [Fact]
        public async Task Starting_a_strategy_replaces_the_running_one_after_its_reset()
        {
            var client = new FakeChaosClient();
            var (runner, _) = CreateRunner(client, new()
            {
                ["a"] = OneStep(Constants.Resources.ExternalImages, Constants.Chaos.Slow),
                ["b"] = OneStep(Constants.Resources.Api, Constants.Chaos.Down)
            });

            await runner.RunAsync("a", Cancellation);
            await WaitForRounds(client, 1);

            await runner.RunAsync("b", Cancellation);
            await WaitForRounds(client, 3);

            Round(client, 0)[Constants.Resources.ExternalImages].ShouldBe(Constants.Chaos.Slow);
            IsReset(Round(client, 1)).ShouldBeTrue();
            Round(client, 2)[Constants.Resources.Api].ShouldBe(Constants.Chaos.Down);
            runner.Status.Strategy.ShouldBe("b");

            await runner.CancelAsync(Cancellation);
        }

        [Fact]
        public async Task Next_step_skips_the_wait()
        {
            var client = new FakeChaosClient();
            var (runner, _) = CreateRunner(client, new() { ["test"] = TwoSteps(Long) });

            await runner.RunAsync("test", Cancellation);
            await WaitForRounds(client, 1);
            runner.Status.ShouldBe(new StrategyStatus("test", 1, "One"));

            runner.SkipStep();
            await WaitForRounds(client, 2);

            Round(client, 1)[Constants.Resources.Api].ShouldBe(Constants.Chaos.Flaky);
            runner.Status.Step.ShouldBe(2);

            await runner.CancelAsync(Cancellation);
        }

        [Fact]
        public async Task Next_step_on_the_last_step_finishes_and_resets()
        {
            var client = new FakeChaosClient();
            var (runner, _) = CreateRunner(client, new() { ["test"] = TwoSteps(Long) });

            await runner.RunAsync("test", Cancellation);
            await WaitForRounds(client, 1);
            runner.SkipStep();
            await WaitForRounds(client, 2);
            runner.SkipStep();
            await runner.Completion.WaitAsync(Cancellation);

            client.Calls.Count.ShouldBe(3 * RoundSize);
            IsReset(Round(client, 2)).ShouldBeTrue();
            runner.Status.ShouldBe(new StrategyStatus(null, null, null));
        }

        [Fact]
        public async Task Cancel_resets_and_cancel_when_idle_does_nothing()
        {
            var client = new FakeChaosClient();
            var (runner, _) = CreateRunner(client, new() { ["test"] = TwoSteps(Long) });

            await runner.CancelAsync(Cancellation);
            runner.SkipStep();
            client.Calls.ShouldBeEmpty();

            await runner.RunAsync("test", Cancellation);
            await WaitForRounds(client, 1);
            await runner.CancelAsync(Cancellation);

            client.Calls.Count.ShouldBe(2 * RoundSize);
            IsReset(Round(client, 1)).ShouldBeTrue();
            runner.Status.Strategy.ShouldBeNull();

            await runner.CancelAsync(Cancellation);
            client.Calls.Count.ShouldBe(2 * RoundSize);
        }

        [Fact]
        public async Task An_unreachable_caller_does_not_stop_the_others()
        {
            var client = new FakeChaosClient(unreachable: Constants.Resources.CallbackNotifier);
            var (runner, meterFactory) = CreateRunner(client, new() { ["test"] = TwoSteps(TimeSpan.FromMilliseconds(10)) }, TimeProvider.System);
            using var switches = new MetricCollector<long>(meterFactory, Constants.Telemetry.ChaosMonkeyMeterName, "chaos.monkey.switches");

            await runner.RunAsync("test", Cancellation);
            await runner.Completion;

            client.Calls.Count.ShouldBe(3 * RoundSize);
            switches.GetMeasurementSnapshot()
                .Where(m => (string?)m.Tags["chaos.result"] == "failed")
                .ShouldAllBe(m => (string?)m.Tags["chaos.target"] == Constants.Resources.CallbackNotifier);
            switches.GetMeasurementSnapshot().Count(m => (string?)m.Tags["chaos.result"] == "failed").ShouldBe(3);
        }

        [Fact]
        public async Task Stopping_the_host_resets()
        {
            var client = new FakeChaosClient();
            var (runner, _) = CreateRunner(client, new() { ["test"] = TwoSteps(Long, repeat: true) });

            await runner.RunAsync("test", Cancellation);
            await WaitForRounds(client, 1);
            await ((IHostedService)runner).StopAsync(Cancellation);

            client.Calls.Count.ShouldBe(2 * RoundSize);
            IsReset(Round(client, 1)).ShouldBeTrue();
        }
    }
}

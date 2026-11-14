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
        private static readonly int RoundSize = ChaosTargets.Scenarios.Count + ResilienceTargets.Names.Count + ConsumptionTargets.Names.Count;

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

        // Step 1 breaks the image analysis, turns its caller's resilience off and pauses the image downloads;
        // step 2 breaks the Api.
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
                    Resilience = { [Constants.Resources.ExternalImageProcessor] = Constants.Resilience.None },
                    Consumption = { [Constants.Resources.ImageDownloads] = Constants.Consumption.Paused }
                },
                new() { Name = "Two", Duration = duration, Chaos = { [Constants.Resources.Api] = Constants.Chaos.Flaky } }
            ]
        };

        private static StrategyOptions OneStep(string target, string scenario) => new()
        {
            Steps = [new() { Duration = Long, Chaos = { [target] = scenario } }]
        };

        // One switch of every chaos target and caller, as target -> value.
        private static Dictionary<string, string> Round(FakeChaosClient client, int round) =>
            client.Calls.Skip(round * RoundSize).Take(RoundSize)
                .Where(c => c.Kind != FakeChaosClient.Consumption)
                .ToDictionary(c => c.Target, c => c.Value);

        // One consumption switch of every worker, as worker -> state.
        private static Dictionary<string, string> Workers(FakeChaosClient client, int round) =>
            client.Calls.Skip(round * RoundSize).Take(RoundSize)
                .Where(c => c.Kind == FakeChaosClient.Consumption)
                .ToDictionary(c => c.Target, c => c.Value);

        private static bool IsReset(FakeChaosClient client, int number)
        {
            var round = Round(client, number);
            var workers = Workers(client, number);

            return round.Count + workers.Count == RoundSize &&
                ConsumptionTargets.Names.All(w => workers[w] == Constants.Consumption.Running) &&
            ChaosTargets.Names.All(t => round[t] == Constants.Chaos.Healthy) &&
                ResilienceTargets.Names.All(c => round[c] == Constants.Resilience.Standard);
        }

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

            var paused = Workers(client, 0);
            paused[Constants.Resources.ImageDownloads].ShouldBe(Constants.Consumption.Paused);
            ConsumptionTargets.Names.Where(w => w != Constants.Resources.ImageDownloads).ShouldAllBe(w => paused[w] == Constants.Consumption.Running);
            Workers(client, 1).Values.ShouldAllBe(s => s == Constants.Consumption.Running);

            var second = Round(client, 1);
            second[Constants.Resources.Api].ShouldBe(Constants.Chaos.Flaky);
            second[Constants.Resources.ExternalImageProcessor].ShouldBe(Constants.Resilience.Standard);

            IsReset(client, 2).ShouldBeTrue();

            var measurements = switches.GetMeasurementSnapshot();
            measurements.Count.ShouldBe(client.Calls.Count);
            measurements.ShouldContain(m => (string?)m.Tags["chaos.kind"] == FakeChaosClient.Resilience);
            measurements.ShouldContain(m => (string?)m.Tags["chaos.kind"] == FakeChaosClient.Consumption);
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
            IsReset(client, 1).ShouldBeTrue();
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
            IsReset(client, 2).ShouldBeTrue();
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
            IsReset(client, 1).ShouldBeTrue();
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
            switches.GetMeasurementSnapshot().Count(m => (string?)m.Tags["chaos.result"] == "failed").ShouldBe(3 * 2); // Its resilience and consumption switches, in 3 rounds.
        }

        [Fact]
        public async Task Stopping_during_a_pause_resumes_the_worker()
        {
            var client = new FakeChaosClient();
            var (runner, _) = CreateRunner(client, new() { ["test"] = TwoSteps(Long) });

            await runner.RunAsync("test", Cancellation);
            await WaitForRounds(client, 1);
            Workers(client, 0)[Constants.Resources.ImageDownloads].ShouldBe(Constants.Consumption.Paused);

            await runner.CancelAsync(Cancellation);

            client.Calls.Last(c => c.Target == Constants.Resources.ImageDownloads && c.Kind == FakeChaosClient.Consumption)
                .Value.ShouldBe(Constants.Consumption.Running);
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
            IsReset(client, 1).ShouldBeTrue();
        }

        [Fact]
        public async Task A_switch_that_throws_is_counted_as_failed_and_the_step_goes_on()
        {
            var client = new FakeChaosClient
            {
                Throw = (_, target) => target == Constants.Resources.CallbackNotifier ? new InvalidOperationException("Rejected") : null
            };
            var (runner, meterFactory) = CreateRunner(client, new() { ["test"] = TwoSteps(TimeSpan.FromMilliseconds(10)) }, TimeProvider.System);
            using var switches = new MetricCollector<long>(meterFactory, Constants.Telemetry.ChaosMonkeyMeterName, "chaos.monkey.switches");

            await runner.RunAsync("test", Cancellation);
            await runner.Completion;

            client.Calls.Count.ShouldBe(3 * RoundSize);
            switches.GetMeasurementSnapshot().Count(m => (string?)m.Tags["chaos.result"] == "failed").ShouldBe(3 * 2); // Its resilience and consumption switches, in 3 rounds.
            runner.Status.ShouldBe(StrategyStatus.Idle);
        }

        [Fact]
        public async Task A_reset_that_fails_does_not_jam_the_runner()
        {
            // Every call after the first step's round fails, as when the reset times out.
            var client = new FakeChaosClient { Throw = (call, _) => call >= RoundSize ? new OperationCanceledException() : null };
            var (runner, _) = CreateRunner(client, new() { ["test"] = OneStep(Constants.Resources.Api, Constants.Chaos.Down) });

            await runner.RunAsync("test", Cancellation);
            await WaitForRounds(client, 1);
            await runner.CancelAsync(Cancellation);

            (await runner.RunAsync("test", Cancellation)).ShouldBeTrue();
            await runner.CancelAsync(Cancellation);
            runner.Status.ShouldBe(StrategyStatus.Idle);
        }
    }
}

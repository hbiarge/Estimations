using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Acheve.Common.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Acheve.Tools.ChaosMonkey.Tests
{
    public class ChaosMonkeyWorkerTests
    {
        private sealed class FakeClient(params string[] unreachable) : IChaosClient
        {
            public ConcurrentQueue<(string Target, string Scenario)> Calls { get; } = new();

            public Task<bool> SetScenarioAsync(string target, string scenario, CancellationToken cancellationToken)
            {
                Calls.Enqueue((target, scenario));
                return Task.FromResult(!unreachable.Contains(target));
            }
        }

        private static (ChaosMonkeyWorker Worker, IMeterFactory MeterFactory) CreateWorker(IChaosClient client, ChaosMonkeyOptions options)
        {
            var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
            var worker = new ChaosMonkeyWorker(
                client,
                new ChaosMonkeyMetrics(meterFactory),
                Options.Create(options),
                TimeProvider.System,
                new Random(1),
                NullLogger<ChaosMonkeyWorker>.Instance);

            return (worker, meterFactory);
        }

        private static ChaosMonkeyOptions TwoSteps(bool repeat, TimeSpan duration) => new()
        {
            Strategies =
            {
                [Constants.ChaosMonkey.Tour] = new()
                {
                    Repeat = repeat,
                    Steps =
                    [
                        new() { Duration = duration, Chaos = { [Constants.Resources.ExternalImageProcess] = Constants.Chaos.LostCallbacks } },
                        new() { Duration = duration, Chaos = { [Constants.Resources.Api] = Constants.Chaos.Flaky } }
                    ]
                }
            }
        };

        // The calls of one switch of every target, as target -> scenario.
        private static Dictionary<string, string> Round(IEnumerable<(string Target, string Scenario)> calls, int round) =>
            calls.Skip(round * ChaosTargets.Scenarios.Count).Take(ChaosTargets.Scenarios.Count).ToDictionary(c => c.Target, c => c.Scenario);

        [Fact]
        public async Task Switches_every_target_at_each_step_and_resets_them_at_the_end()
        {
            var client = new FakeClient();
            var (worker, meterFactory) = CreateWorker(client, TwoSteps(repeat: false, TimeSpan.FromMilliseconds(20)));
            using var switches = new MetricCollector<long>(meterFactory, Constants.Telemetry.ChaosMonkeyMeterName, "chaos.monkey.switches");

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await worker.ExecuteTask!;

            var calls = client.Calls.ToList();
            calls.Count.ShouldBe(3 * ChaosTargets.Scenarios.Count);

            var first = Round(calls, 0);
            first[Constants.Resources.ExternalImageProcess].ShouldBe(Constants.Chaos.LostCallbacks);
            first.Where(c => c.Key != Constants.Resources.ExternalImageProcess).ShouldAllBe(c => c.Value == Constants.Chaos.Healthy);

            var second = Round(calls, 1);
            second[Constants.Resources.Api].ShouldBe(Constants.Chaos.Flaky);
            second[Constants.Resources.ExternalImageProcess].ShouldBe(Constants.Chaos.Healthy);

            Round(calls, 2).Values.ShouldAllBe(s => s == Constants.Chaos.Healthy);
            switches.GetMeasurementSnapshot().Count.ShouldBe(calls.Count);
        }

        [Fact]
        public async Task An_unreachable_target_does_not_stop_the_others()
        {
            var client = new FakeClient(unreachable: Constants.Resources.ExternalNotifications);
            var (worker, meterFactory) = CreateWorker(client, TwoSteps(repeat: false, TimeSpan.FromMilliseconds(10)));
            using var switches = new MetricCollector<long>(meterFactory, Constants.Telemetry.ChaosMonkeyMeterName, "chaos.monkey.switches");

            await worker.StartAsync(TestContext.Current.CancellationToken);
            await worker.ExecuteTask!;

            var failed = switches.GetMeasurementSnapshot().Where(m => (string)m.Tags["chaos.result"]! == "failed").ToList();
            failed.Count.ShouldBe(3);
            failed.ShouldAllBe(m => (string)m.Tags["chaos.target"]! == Constants.Resources.ExternalNotifications);
            client.Calls.Count.ShouldBe(3 * ChaosTargets.Scenarios.Count);
        }

        [Fact]
        public async Task Stopping_the_monkey_puts_every_target_back_to_healthy()
        {
            var client = new FakeClient();
            var (worker, _) = CreateWorker(client, TwoSteps(repeat: true, TimeSpan.FromMinutes(10)));

            await worker.StartAsync(TestContext.Current.CancellationToken);
            while (client.Calls.Count < ChaosTargets.Scenarios.Count)
            {
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }

            await worker.StopAsync(TestContext.Current.CancellationToken);

            var calls = client.Calls.ToList();
            calls.Count.ShouldBe(2 * ChaosTargets.Scenarios.Count);
            Round(calls, 1).Values.ShouldAllBe(s => s == Constants.Chaos.Healthy);
        }
    }
}

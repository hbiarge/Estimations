using System.Diagnostics.Metrics;
using Acheve.Common.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Acheve.Tools.TrafficGenerator.Tests
{
    public class CaseRunnerTests
    {
        private static readonly NewEstimationRequest Request = new("https://client/callback", ["https://images/1.png"]);

        // Answers the polls with the given statuses, in order; null is a failed poll. The last one repeats.
        private sealed class FakeClient(string? ticket, params CaseStatus?[] states) : IEstimationsClient
        {
            private int _polls;

            public int Polls => _polls;

            public Task<string?> StartAsync(NewEstimationRequest request, CancellationToken cancellationToken) =>
                Task.FromResult(ticket);

            public Task<CaseStatus?> GetStateAsync(string ticket, CancellationToken cancellationToken)
            {
                var index = Math.Min(_polls++, states.Length - 1);
                return Task.FromResult(states[index]);
            }
        }

        private static (CaseRunner Runner, IMeterFactory MeterFactory) CreateRunner(IEstimationsClient client, TimeSpan? caseTimeout = null)
        {
            var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
            var options = Options.Create(new TrafficOptions
            {
                PollInterval = TimeSpan.FromMilliseconds(1),
                CaseTimeout = caseTimeout ?? TimeSpan.FromSeconds(10)
            });

            var runner = new CaseRunner(client, new TrafficMetrics(meterFactory), options, TimeProvider.System, NullLogger<CaseRunner>.Instance);
            return (runner, meterFactory);
        }

        [Fact]
        public async Task Follows_a_case_to_its_end_and_records_the_outcome()
        {
            var client = new FakeClient(
                "ticket",
                new CaseStatus("WaitingForImagesToBeDownloaded"),
                new CaseStatus("WaitingForEstimation"),
                new CaseStatus("EstimationReady", "Success"),
                new CaseStatus("ClientNotified", "Success"));
            var (runner, meterFactory) = CreateRunner(client);
            using var finished = new MetricCollector<long>(meterFactory, Constants.Telemetry.TrafficMeterName, "traffic.cases.finished");
            using var duration = new MetricCollector<double>(meterFactory, Constants.Telemetry.TrafficMeterName, "traffic.case.duration");
            using var inFlight = new MetricCollector<long>(meterFactory, Constants.Telemetry.TrafficMeterName, "traffic.cases.in_flight");

            var outcome = await runner.RunAsync(Request, TestContext.Current.CancellationToken);

            outcome.ShouldBe(CaseOutcome.Succeeded);
            client.Polls.ShouldBe(4);
            finished.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["traffic.outcome"].ShouldBe("succeeded");
            duration.GetMeasurementSnapshot().ShouldHaveSingleItem();
            inFlight.GetMeasurementSnapshot().Sum(m => m.Value).ShouldBe(0);
        }

        [Fact]
        public async Task A_failed_poll_waits_for_the_next_one()
        {
            var client = new FakeClient("ticket", new CaseStatus("EstimationError", "Failure"), null, new CaseStatus("ClientNotified", "Failure"));
            var (runner, _) = CreateRunner(client);

            var outcome = await runner.RunAsync(Request, TestContext.Current.CancellationToken);

            outcome.ShouldBe(CaseOutcome.Failed);
        }

        [Fact]
        public async Task A_case_the_Api_does_not_accept_is_rejected_without_polling()
        {
            var client = new FakeClient(ticket: null, new CaseStatus("ClientNotified", "Success"));
            var (runner, meterFactory) = CreateRunner(client);
            using var duration = new MetricCollector<double>(meterFactory, Constants.Telemetry.TrafficMeterName, "traffic.case.duration");

            var outcome = await runner.RunAsync(Request, TestContext.Current.CancellationToken);

            outcome.ShouldBe(CaseOutcome.Rejected);
            client.Polls.ShouldBe(0);
            duration.GetMeasurementSnapshot().ShouldBeEmpty();
        }

        [Fact]
        public async Task A_case_without_a_final_state_times_out()
        {
            var client = new FakeClient("ticket", new CaseStatus("WaitingForImagesToBeAnalysed"));
            var (runner, _) = CreateRunner(client, caseTimeout: TimeSpan.FromMilliseconds(100));

            var outcome = await runner.RunAsync(Request, TestContext.Current.CancellationToken);

            outcome.ShouldBe(CaseOutcome.TimedOut);
        }
    }
}

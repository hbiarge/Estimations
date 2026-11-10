using Acheve.Common.Messages;
using Microsoft.Extensions.Time.Testing;

namespace Acheve.Application.StateHolder.Tests
{
    // The running-case gauges the StateHolder reports for the Grafana dashboards.
    public class StateTests
    {
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        private readonly State _sut;

        public StateTests()
        {
            _sut = new State(_time);
        }

        [Fact]
        public void Running_cases_are_counted_by_state_including_empty_states()
        {
            _sut.AddOrUpdateState("case-1", "client", EstimationStates.WaitingForImagesToBeDownloaded);
            _sut.AddOrUpdateState("case-2", "client", EstimationStates.WaitingForEstimation);
            _sut.AddOrUpdateState("case-3", "client", EstimationStates.WaitingForEstimation);
            _sut.AddOrUpdateState("case-4", "client", EstimationStates.ClientNotified);

            var counts = _sut.ActiveCasesByState();

            counts.Keys.ShouldBe(State.ActiveStates, ignoreOrder: true);
            counts[EstimationStates.WaitingForImagesToBeDownloaded].ShouldBe(1);
            counts[EstimationStates.WaitingForImagesToBeAnalysed].ShouldBe(0);
            counts[EstimationStates.WaitingForEstimation].ShouldBe(2);
            counts.ContainsKey(EstimationStates.ClientNotified).ShouldBeFalse();
        }

        [Fact]
        public void Oldest_running_case_age_counts_from_when_the_case_was_first_seen()
        {
            _sut.AddOrUpdateState("old", "client", EstimationStates.WaitingForImagesToBeDownloaded);
            _time.Advance(TimeSpan.FromSeconds(30));
            _sut.AddOrUpdateState("old", "client", EstimationStates.WaitingForEstimation);
            _sut.AddOrUpdateState("new", "client", EstimationStates.WaitingForImagesToBeDownloaded);
            _time.Advance(TimeSpan.FromSeconds(10));

            _sut.OldestActiveCaseAge().ShouldBe(TimeSpan.FromSeconds(40));
        }

        [Fact]
        public void Finished_cases_do_not_count_as_running()
        {
            _sut.AddOrUpdateState("done", "client", EstimationStates.WaitingForImagesToBeDownloaded);
            _time.Advance(TimeSpan.FromMinutes(5));
            _sut.AddOrUpdateState("done", "client", EstimationStates.ClientNotificationError);

            _sut.OldestActiveCaseAge().ShouldBe(TimeSpan.Zero);
            _sut.ActiveCasesByState().Values.Sum().ShouldBe(0);
        }

        [Fact]
        public void State_only_moves_forward()
        {
            _sut.AddOrUpdateState("case", "client", EstimationStates.WaitingForEstimation);
            _sut.AddOrUpdateState("case", "client", EstimationStates.WaitingForImagesToBeDownloaded);

            _sut.GetState("case", "client").ShouldBe(nameof(EstimationStates.WaitingForEstimation));
            _sut.GetState("other", "client").ShouldBe("Unknown ticket");
        }
    }
}

using Acheve.Application.StateHolder;
using Acheve.Common.Messages;
using Microsoft.Extensions.Time.Testing;

namespace Acheve.Application.StateHolder.Tests
{
    // The running-case gauges the StateHolder reports for the Grafana dashboards, and the state it answers queries with.
    public class StateTests
    {
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        private readonly State _sut;

        public StateTests()
        {
            _sut = new State(_time);
        }

        private EstimationStateChanged Change(
            Guid ticket,
            EstimationStates state,
            string? result = null,
            string? failureReason = null,
            string? estimationTicket = null) => new()
            {
                CaseNumber = ticket,
                ClientId = "client",
                State = state,
                Result = result,
                FailureReason = failureReason,
                EstimationTicket = estimationTicket
            };

        [Fact]
        public void Running_cases_are_counted_by_state_including_empty_states()
        {
            _sut.AddOrUpdateState(Change(Guid.NewGuid(), EstimationStates.WaitingForImagesToBeDownloaded));
            _sut.AddOrUpdateState(Change(Guid.NewGuid(), EstimationStates.WaitingForEstimation));
            _sut.AddOrUpdateState(Change(Guid.NewGuid(), EstimationStates.WaitingForEstimation));
            _sut.AddOrUpdateState(Change(Guid.NewGuid(), EstimationStates.ClientNotified));

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
            var old = Guid.NewGuid();
            _sut.AddOrUpdateState(Change(old, EstimationStates.WaitingForImagesToBeDownloaded));
            _time.Advance(TimeSpan.FromSeconds(30));
            _sut.AddOrUpdateState(Change(old, EstimationStates.WaitingForEstimation));
            _sut.AddOrUpdateState(Change(Guid.NewGuid(), EstimationStates.WaitingForImagesToBeDownloaded));
            _time.Advance(TimeSpan.FromSeconds(10));

            _sut.OldestActiveCaseAge().ShouldBe(TimeSpan.FromSeconds(40));
        }

        [Fact]
        public void Finished_cases_do_not_count_as_running()
        {
            var done = Guid.NewGuid();
            _sut.AddOrUpdateState(Change(done, EstimationStates.WaitingForImagesToBeDownloaded));
            _time.Advance(TimeSpan.FromMinutes(5));
            _sut.AddOrUpdateState(Change(done, EstimationStates.ClientNotificationError));

            _sut.OldestActiveCaseAge().ShouldBe(TimeSpan.Zero);
            _sut.ActiveCasesByState().Values.Sum().ShouldBe(0);
        }

        [Fact]
        public void State_only_moves_forward_and_keeps_the_result_of_the_furthest_state()
        {
            var ticket = Guid.NewGuid();
            _sut.AddOrUpdateState(Change(ticket, EstimationStates.EstimationReady, "Success", estimationTicket: "estimation"));
            _sut.AddOrUpdateState(Change(ticket, EstimationStates.WaitingForEstimation));

            var snapshot = _sut.GetState(ticket.ToString("D"), "client").ShouldNotBeNull();
            snapshot.State.ShouldBe(EstimationStates.EstimationReady);
            snapshot.Result.ShouldBe("Success");
            snapshot.EstimationTicket.ShouldBe("estimation");
        }

        [Fact]
        public void An_unknown_ticket_or_another_clients_ticket_is_not_found()
        {
            var ticket = Guid.NewGuid();
            _sut.AddOrUpdateState(Change(ticket, EstimationStates.WaitingForEstimation));

            _sut.GetState(Guid.NewGuid().ToString("D"), "client").ShouldBeNull();
            _sut.GetState(ticket.ToString("D"), "another-client").ShouldBeNull();
        }

        [Fact]
        public void Loaded_cases_are_answered_and_do_not_move_a_case_backwards()
        {
            var ticket = Guid.NewGuid();
            _sut.AddOrUpdateState(Change(ticket, EstimationStates.ClientNotified, "Failure", "no images"));

            _sut.Load(
            [
                new CaseSnapshot
                {
                    Ticket = ticket.ToString("D"),
                    ClientId = "client",
                    State = EstimationStates.WaitingForEstimation,
                    FirstSeen = _time.GetUtcNow().AddMinutes(-10),
                    UpdatedAt = _time.GetUtcNow().AddMinutes(-10)
                }
            ]);

            var snapshot = _sut.GetState(ticket.ToString("D"), "client").ShouldNotBeNull();
            snapshot.State.ShouldBe(EstimationStates.ClientNotified);
            snapshot.FailureReason.ShouldBe("no images");
            snapshot.FirstSeen.ShouldBe(_time.GetUtcNow().AddMinutes(-10));
        }

        [Fact]
        public void Finished_cases_are_forgotten_after_their_retention_time_but_running_ones_never()
        {
            var finished = Guid.NewGuid();
            var running = Guid.NewGuid();
            _sut.AddOrUpdateState(Change(finished, EstimationStates.ClientNotified));
            _sut.AddOrUpdateState(Change(running, EstimationStates.WaitingForEstimation));
            _time.Advance(TimeSpan.FromHours(25));

            _sut.RemoveFinishedBefore(_time.GetUtcNow().AddHours(-24)).ShouldBe(1);

            _sut.GetState(finished.ToString("D"), "client").ShouldBeNull();
            _sut.GetState(running.ToString("D"), "client").ShouldNotBeNull();
        }
    }
}

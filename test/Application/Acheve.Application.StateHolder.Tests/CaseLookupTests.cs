using Acheve.Application.StateHolder;
using Acheve.Common.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Acheve.Application.StateHolder.Tests
{
    // How a query finds a case: memory for a finished one, the database for any other.
    public class CaseLookupTests
    {
        private const string Ticket = "11111111-1111-1111-1111-111111111111";

        private sealed class FakeStore : ICaseStateStore
        {
            public Dictionary<(string Ticket, string CustomerId), CaseSnapshot> Cases { get; } = [];

            public int Reads { get; private set; }

            public bool Down { get; set; }

            public Task Initialize(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task Save(CaseSnapshot snapshot, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<IReadOnlyList<CaseSnapshot>> LoadAll(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<CaseSnapshot>>(Cases.Values.ToList());

            public Task DeleteFinishedBefore(DateTimeOffset cutoff, CancellationToken cancellationToken) => Task.CompletedTask;

            public Task<CaseSnapshot?> Get(string ticket, string customerId, CancellationToken cancellationToken)
            {
                Reads++;

                if (Down)
                {
                    throw new InvalidOperationException("The database is down.");
                }

                return Task.FromResult(Cases.GetValueOrDefault((ticket, customerId)));
            }
        }

        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        private readonly FakeStore _store = new();
        private readonly State _state;
        private readonly CaseLookup _sut;

        public CaseLookupTests()
        {
            _state = new State(_time);
            _sut = new CaseLookup(_state, _store, NullLogger<CaseLookup>.Instance);
        }

        private CaseSnapshot Snapshot(EstimationStates state, string customerId = "acme") => new()
        {
            Ticket = Ticket,
            CustomerId = customerId,
            State = state,
            FirstSeen = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow()
        };

        private void InMemory(EstimationStates state) =>
            _state.AddOrUpdateState(new EstimationStateChanged
            {
                CaseNumber = Guid.Parse(Ticket),
                CustomerId = "acme",
                State = state
            });

        [Fact]
        public async Task Finds_a_case_this_instance_never_saw_in_the_database()
        {
            _store.Cases[(Ticket, "acme")] = Snapshot(EstimationStates.WaitingForEstimation);

            var found = await _sut.Find(Ticket, "acme", CancellationToken.None);

            found.ShouldNotBeNull().State.ShouldBe(EstimationStates.WaitingForEstimation);
            // And remembers it.
            _state.GetState(Ticket, "acme").ShouldNotBeNull();
        }

        [Fact]
        public async Task Reads_a_running_case_from_the_database_because_memory_can_be_behind()
        {
            InMemory(EstimationStates.WaitingForImagesToBeDownloaded);
            _store.Cases[(Ticket, "acme")] = Snapshot(EstimationStates.WaitingForEstimation);

            var found = await _sut.Find(Ticket, "acme", CancellationToken.None);

            found.ShouldNotBeNull().State.ShouldBe(EstimationStates.WaitingForEstimation);
            _store.Reads.ShouldBe(1);
        }

        [Fact]
        public async Task Answers_a_finished_case_from_memory_without_reading_the_database()
        {
            InMemory(EstimationStates.CustomerNotified);

            var found = await _sut.Find(Ticket, "acme", CancellationToken.None);

            found.ShouldNotBeNull().State.ShouldBe(EstimationStates.CustomerNotified);
            _store.Reads.ShouldBe(0);
        }

        [Fact]
        public async Task Keeps_the_state_in_memory_when_it_is_ahead_of_the_database()
        {
            InMemory(EstimationStates.WaitingForEstimation);
            _store.Cases[(Ticket, "acme")] = Snapshot(EstimationStates.WaitingForImagesToBeAnalysed);

            var found = await _sut.Find(Ticket, "acme", CancellationToken.None);

            found.ShouldNotBeNull().State.ShouldBe(EstimationStates.WaitingForEstimation);
        }

        [Fact]
        public async Task Does_not_show_a_case_to_another_customer()
        {
            _store.Cases[(Ticket, "acme")] = Snapshot(EstimationStates.WaitingForEstimation);

            var found = await _sut.Find(Ticket, "globex", CancellationToken.None);

            found.ShouldBeNull();
        }

        [Fact]
        public async Task Answers_with_memory_when_the_database_is_down()
        {
            InMemory(EstimationStates.WaitingForEstimation);
            _store.Down = true;

            var found = await _sut.Find(Ticket, "acme", CancellationToken.None);

            found.ShouldNotBeNull().State.ShouldBe(EstimationStates.WaitingForEstimation);
        }

        [Fact]
        public async Task Fails_when_the_database_is_down_and_memory_has_nothing()
        {
            _store.Down = true;

            await Should.ThrowAsync<InvalidOperationException>(
                () => _sut.Find(Ticket, "acme", CancellationToken.None));
        }
    }
}

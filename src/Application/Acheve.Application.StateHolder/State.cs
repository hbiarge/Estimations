using System.Collections.Concurrent;
using Acheve.Common.Messages;

namespace Acheve.Application.StateHolder;

// What the state holder knows about one case.
public sealed record CaseSnapshot
{
    public required string Ticket { get; init; }

    public required string ClientId { get; init; }

    public required EstimationStates State { get; init; }

    public required DateTimeOffset FirstSeen { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    // A CaseResults value once the case has an estimation or has failed.
    public string? Result { get; init; }

    public string? FailureReason { get; init; }

    public string? EstimationTicket { get; init; }

    public IReadOnlyList<SkippedImage> SkippedImages { get; init; } = [];

    public bool Finished => State >= EstimationStates.ClientNotified;
}

// The latest state of every case, in memory. The CaseStateStore keeps a copy in PostgreSQL,
// loaded again at startup, so a restart doesn't forget the cases.
public class State(TimeProvider timeProvider)
{
    // States in which a case is still running. Cases in a later state are finished.
    public static readonly EstimationStates[] ActiveStates = Enum.GetValues<EstimationStates>()
        .Where(x => x < EstimationStates.ClientNotified)
        .ToArray();

    // The key is the ticket concatenated with the client id
    private readonly ConcurrentDictionary<string, CaseSnapshot> _state = new();

    // The case, or null when this client has no case with this ticket.
    public CaseSnapshot? GetState(string ticket, string clientId) =>
        _state.GetValueOrDefault(GetKey(ticket, clientId));

    // Applies a state change and returns what is now known about the case.
    // States only move forward: a change to a state that is not higher than the current one changes nothing.
    public CaseSnapshot AddOrUpdateState(EstimationStateChanged change)
    {
        var now = timeProvider.GetUtcNow();

        return Merge(new CaseSnapshot
        {
            Ticket = change.CaseNumber.ToString("D"),
            ClientId = change.ClientId,
            State = change.State,
            FirstSeen = now,
            UpdatedAt = now,
            Result = change.Result,
            FailureReason = change.FailureReason,
            EstimationTicket = change.EstimationTicket,
            SkippedImages = change.SkippedImages?.ToArray() ?? []
        });
    }

    // Adds the cases read from the store at startup. A case that already moved further in memory stays as it is.
    public void Load(IEnumerable<CaseSnapshot> cases)
    {
        foreach (var snapshot in cases)
        {
            Merge(snapshot);
        }
    }

    // Forgets the cases that finished before the given time. Returns how many.
    public int RemoveFinishedBefore(DateTimeOffset cutoff)
    {
        var removed = 0;

        foreach (var (key, snapshot) in _state)
        {
            if (snapshot.Finished
                && snapshot.UpdatedAt < cutoff
                && _state.TryRemove(new KeyValuePair<string, CaseSnapshot>(key, snapshot)))
            {
                removed++;
            }
        }

        return removed;
    }

    // Running cases per state, including states with no cases (for the metrics).
    public IReadOnlyDictionary<EstimationStates, int> ActiveCasesByState()
    {
        var counts = ActiveStates.ToDictionary(x => x, _ => 0);

        foreach (var snapshot in _state.Values)
        {
            if (counts.ContainsKey(snapshot.State))
            {
                counts[snapshot.State] += 1;
            }
        }

        return counts;
    }

    // How long ago the oldest running case was first seen, or zero when no case is running.
    // A case normally finishes within a few minutes; a much older one is stuck.
    public TimeSpan OldestActiveCaseAge()
    {
        var oldest = _state.Values
            .Where(x => !x.Finished)
            .Select(x => (DateTimeOffset?)x.FirstSeen)
            .Min();

        return oldest is null ? TimeSpan.Zero : timeProvider.GetUtcNow() - oldest.Value;
    }

    // The saga sends everything it knows with every change, so the snapshot with the higher state is the
    // complete one. Only the time the case was first seen is kept from the other.
    private CaseSnapshot Merge(CaseSnapshot incoming) =>
        _state.AddOrUpdate(
            key: GetKey(incoming.Ticket, incoming.ClientId),
            addValue: incoming,
            updateValueFactory: (_, existing) =>
            {
                var firstSeen = existing.FirstSeen < incoming.FirstSeen ? existing.FirstSeen : incoming.FirstSeen;

                return incoming.State > existing.State
                    ? incoming with { FirstSeen = firstSeen }
                    : existing with { FirstSeen = firstSeen };
            });

    private static string GetKey(string ticket, string clientId)
    {
        return $"{ticket}-{clientId}";
    }
}

using System.Collections.Concurrent;
using Acheve.Common.Messages;

namespace Acheve.Application.StateHolder;

public class State(TimeProvider timeProvider)
{
    // States in which a case is still running. Cases in a later state are finished.
    public static readonly EstimationStates[] ActiveStates = Enum.GetValues<EstimationStates>()
        .Where(x => x < EstimationStates.ClientNotified)
        .ToArray();

    // The key is the ticket concatenated with the client id
    private readonly ConcurrentDictionary<string, CaseState> _state = new();

    public string GetState(string ticket, string clientId)
    {
        var key = GetKey(ticket, clientId);

        if (_state.TryGetValue(key, out var caseState))
        {
            return caseState.State.ToString("G");
        }

        return "Unknown ticket";
    }

    public void AddOrUpdateState(string ticket, string clientId, EstimationStates state)
    {
        var key = GetKey(ticket, clientId);

        _ = _state.AddOrUpdate(
            key: key,
            addValue: new CaseState(state, timeProvider.GetUtcNow()),
            updateValueFactory: (key, existingValue) =>
                state > existingValue.State
                    ? existingValue with { State = state }
                    : existingValue);
    }

    // Running cases per state, including states with no cases (for the metrics).
    public IReadOnlyDictionary<EstimationStates, int> ActiveCasesByState()
    {
        var counts = ActiveStates.ToDictionary(x => x, _ => 0);

        foreach (var caseState in _state.Values)
        {
            if (counts.ContainsKey(caseState.State))
            {
                counts[caseState.State] += 1;
            }
        }

        return counts;
    }

    // How long ago the oldest running case was first seen, or zero when no case is running.
    // A case normally finishes within about 4 minutes; a much older one is stuck.
    public TimeSpan OldestActiveCaseAge()
    {
        var oldest = _state.Values
            .Where(x => x.State < EstimationStates.ClientNotified)
            .Select(x => (DateTimeOffset?)x.FirstSeen)
            .Min();

        return oldest is null ? TimeSpan.Zero : timeProvider.GetUtcNow() - oldest.Value;
    }

    private static string GetKey(string ticket, string clientId)
    {
        return $"{ticket}-{clientId}";
    }

    private sealed record CaseState(EstimationStates State, DateTimeOffset FirstSeen);
}

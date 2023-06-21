using System.Collections.Concurrent;
using Acheve.Common.Messages;

namespace Acheve.Application.StateHolder;

public class State
{
    private readonly ConcurrentDictionary<string, EstimationStates> _state = new();

    public string GetState(string ticket)
    {
        if (_state.TryGetValue(ticket, out var state))
        {
            return state.ToString("G");
        }

        return "Unknown ticket";
    }

    public void AddOrUpdateState(string ticket, EstimationStates state)
    {
        _ = _state.AddOrUpdate(
            key: ticket,
            addValue: state,
            updateValueFactory: (key, existingValue) => 
                state > existingValue 
                    ? state 
                    : existingValue);
    }
}
using System.Collections.Concurrent;
using Acheve.Common.Messages;

namespace Acheve.Application.StateHolder;

public class State
{
    // The key is the ticket concatenated with the client id
    private readonly ConcurrentDictionary<string, EstimationStates> _state = new();

    public string GetState(string ticket, string clientId)
    {
        var key = GetKey(ticket, clientId);
        
        if (_state.TryGetValue(key, out var state))
        {
            return state.ToString("G");
        }

        return "Unknown ticket";
    }

    public void AddOrUpdateState(string ticket, string clientId, EstimationStates state)
    {
        var key = GetKey(ticket, clientId);

        _ = _state.AddOrUpdate(
            key: key,
            addValue: state,
            updateValueFactory: (key, existingValue) => 
                state > existingValue 
                    ? state 
                    : existingValue);
    }

    private static string GetKey(string ticket, string clientId)
    {
        return $"{ticket}-{clientId}";
    }
}
namespace Acheve.Application.StateHolder;

/// <summary>
/// Finds a case for a query. The state in memory only has the changes this instance handled, so with several
/// instances (the changes go to whichever takes the message) it can be missing or behind: the database has them all.
/// A finished case doesn't change any more, so memory answers for it; anything else is read from the database.
/// </summary>
public sealed class CaseLookup(State state, ICaseStateStore store, ILogger<CaseLookup> logger)
{
    public async Task<CaseSnapshot?> Find(string ticket, string customerId, CancellationToken cancellationToken)
    {
        var cached = state.GetState(ticket, customerId);

        if (cached is { Finished: true })
        {
            return cached;
        }

        try
        {
            var stored = await store.Get(ticket, customerId, cancellationToken);

            // Memory keeps the higher state: it can already have a change whose save to the database is still running.
            return stored is null ? cached : state.Remember(stored);
        }
        catch (Exception e) when (e is not OperationCanceledException && cached is not null)
        {
            // Better a state that may be one step behind than no answer while the database is down.
            logger.LogWarning(e, "The case {ticket} couldn't be read from the store. Answering with the state in memory.", ticket);

            return cached;
        }
    }
}

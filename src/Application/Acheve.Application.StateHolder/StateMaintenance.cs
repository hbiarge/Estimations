namespace Acheve.Application.StateHolder;

/// <summary>
/// Loads the stored cases into memory at startup, then forgets (in memory and in the store) the cases
/// that finished longer ago than the retention time, so neither grows without limit.
/// </summary>
public sealed class StateMaintenance(
    State state,
    ICaseStateStore store,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<StateMaintenance> logger) : BackgroundService
{
    // How long a finished case can still be queried. The same default as the data bus retention,
    // which decides how long its estimation can be read.
    private readonly TimeSpan _retention = configuration.GetValue("State:Retention", TimeSpan.FromHours(24));
    private readonly TimeSpan _interval = configuration.GetValue("State:CleanupInterval", TimeSpan.FromMinutes(15));

    // Loading is safe next to new state changes: both go through the same forward-only merge.
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await store.Initialize(cancellationToken);

        var cases = await store.LoadAll(cancellationToken);
        state.Load(cases);

        logger.LogInformation("Loaded {count} case(s) from the store.", cases.Count);

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval, timeProvider);

        do
        {
            try
            {
                var cutoff = timeProvider.GetUtcNow() - _retention;

                await store.DeleteFinishedBefore(cutoff, stoppingToken);
                var removed = state.RemoveFinishedBefore(cutoff);

                if (removed > 0)
                {
                    logger.LogInformation(
                        "Forgot {count} case(s) that finished more than {retention} ago.", removed, _retention);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning(e, "The state cleanup failed. It runs again in {interval}.", _interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

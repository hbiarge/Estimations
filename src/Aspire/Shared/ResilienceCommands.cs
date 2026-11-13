using Acheve.Common.Shared;

namespace Aspire.Hosting;

internal static class ResilienceCommands
{
    private static readonly Dictionary<string, (string Description, string Icon)> Details = new()
    {
        [Constants.Resilience.None] = ("No resilience: the first failure or slow answer reaches the handler.", "ShieldDismiss"),
        [Constants.Resilience.RetryOnly] = ("Only retries (3, exponential backoff): no timeouts, no circuit breaker.", "ArrowRepeatAll"),
        [Constants.Resilience.Standard] = ("What AddStandardResilienceHandler() does: concurrency limit, timeouts, retries and circuit breaker.", "ShieldCheckmark"),
        [Constants.Resilience.FailFast] = ("No retries, 3 s attempts and a circuit breaker that opens after a few failures.", "Flash"),
        [Constants.Resilience.Hedging] = ("Another attempt when one takes over a second or fails; the first good answer wins.", "BranchFork"),
    };

    /// <summary>
    /// Adds a dashboard command per resilience profile. Each one calls POST /resilience/profiles/{name} on the service,
    /// which only exists in Development (see Acheve.Common.Resilience).
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithResilienceCommands(this IResourceBuilder<ProjectResource> project)
    {
        foreach (var profile in Constants.Resilience.Profiles)
        {
            var (description, icon) = Details[profile];

            project.WithHttpCommand(
                path: $"/resilience/profiles/{profile}",
                displayName: $"Resilience: {profile}",
                commandName: $"resilience-{profile}",
                commandOptions: new HttpCommandOptions
                {
                    Description = description,
                    IconName = icon,
                    IconVariant = IconVariant.Regular
                });
        }

        return project;
    }
}

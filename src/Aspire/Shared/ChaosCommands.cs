using Acheve.Common.Shared;

namespace Aspire.Hosting;

// Shared by both AppHosts (linked into each project).
internal static class ChaosCommands
{
    private static readonly Dictionary<string, (string Description, string Icon)> Details = new()
    {
        [Constants.Chaos.Healthy] = ("Stop injecting failures.", "CheckmarkCircle"),
        [Constants.Chaos.Flaky] = ("30% of requests fail with 503 and 20% wait 2 s. Mostly hidden by the callers' retries.", "Warning"),
        [Constants.Chaos.Slow] = ("Every request waits 5 s; callbacks come 20 s later than usual.", "Clock"),
        [Constants.Chaos.Down] = ("Every request fails with 503, like an outage.", "DismissCircle"),
        [Constants.Chaos.LostCallbacks] = ("Accepts the work but never calls back for half of it.", "MailDismiss"),
        [Constants.Chaos.DuplicateCallbacks] = ("Calls back twice for half of the work.", "Copy"),
    };

    /// <summary>
    /// Adds a dashboard command per chaos scenario. Each one calls POST /chaos/scenarios/{name} on the service,
    /// which only exists in Development (see Acheve.Common.Chaos).
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithChaosCommands(
        this IResourceBuilder<ProjectResource> project,
        IEnumerable<string> scenarios)
    {
        foreach (var scenario in scenarios)
        {
            var (description, icon) = Details[scenario];

            project.WithHttpCommand(
                path: $"/chaos/scenarios/{scenario}",
                displayName: $"Chaos: {scenario}",
                commandName: $"chaos-{scenario}",
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

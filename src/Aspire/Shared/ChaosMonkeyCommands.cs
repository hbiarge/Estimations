using Acheve.Common.Shared;

namespace Aspire.Hosting;

// The chaos monkey's strategies (Acheve.Tools.ChaosMonkey). Their steps are in the monkey's appsettings.json.
internal static class ChaosMonkeyCommands
{
    private static readonly Dictionary<string, (string Description, string Icon)> Details = new()
    {
        [Constants.ChaosMonkey.Retries] = (
            "Flaky image analysis, first without resilience and then with the standard one: retries hide the failures.",
            "ArrowRepeatAll"),
        [Constants.ChaosMonkey.RetryStorm] = (
            "The images system down, first with retries only (every call becomes four) and then failing fast: the circuit opens.",
            "Flash"),
        [Constants.ChaosMonkey.Hedging] = (
            "Flaky estimations, first with the standard resilience and then hedging: the slow answers stop setting the pace.",
            "BranchFork"),
        [Constants.ChaosMonkey.Timeouts] = (
            "Slow estimations, first with the standard resilience and then failing fast: slow but successful cases fail.",
            "Clock"),
        [Constants.ChaosMonkey.Callbacks] = (
            "Lost and then duplicate callbacks: HTTP resilience can't see them; the saga's timeouts and idempotency handle them.",
            "MailDismiss"),
        [Constants.ChaosMonkey.Backlog] = (
            "The image downloads stop consuming, first for 1 minute (only latency) and then for 3 (the saga gives up: failed cases).",
            "Pause"),
        [Constants.ChaosMonkey.BadDay] = (
            "Several things wrong at once: flaky image analysis, slow estimations, client endpoint down, flaky callbacks.",
            "Warning"),
        [Constants.ChaosMonkey.Tour] = (
            "A tour of the chaos scenarios, about 18 minutes, over and over.",
            "Map"),
        [Constants.ChaosMonkey.Random] = (
            "Random chaos scenarios on random targets, endlessly.",
            "ArrowShuffle"),
    };

    /// <summary>
    /// Adds a dashboard command per strategy (POST /strategies/{name}), plus stop and next step.
    /// The endpoints only exist in Development.
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithStrategyCommands(this IResourceBuilder<ProjectResource> project)
    {
        foreach (var strategy in Constants.ChaosMonkey.Strategies)
        {
            var (description, icon) = Details[strategy];
            project.AddStrategyCommand($"/strategies/{strategy}", $"Strategy: {strategy}", $"strategy-{strategy}", description, icon);
        }

        project.AddStrategyCommand(
            "/strategies/next", "Strategy: next step", "strategy-next", "End the current step now and go on to the next one.", "Next");
        project.AddStrategyCommand(
            "/strategies/stop", "Strategy: stop", "strategy-stop", "Stop the strategy and put every service back to healthy, standard and running.", "Stop");

        return project;
    }

    private static void AddStrategyCommand(
        this IResourceBuilder<ProjectResource> project,
        string path,
        string displayName,
        string commandName,
        string description,
        string icon) =>
        project.WithHttpCommand(
            path: path,
            displayName: displayName,
            commandName: commandName,
            commandOptions: new HttpCommandOptions
            {
                Description = description,
                IconName = icon,
                IconVariant = IconVariant.Regular
            });
}

namespace Aspire.Hosting;

internal static class ConsumptionCommands
{
    /// <summary>
    /// Adds dashboard commands to pause and resume the service's message consumption (POST /consumption/pause and
    /// /consumption/resume), which only exist in Development (see Acheve.Common.Consumption).
    /// </summary>
    public static IResourceBuilder<ProjectResource> WithConsumptionCommands(this IResourceBuilder<ProjectResource> project)
    {
        project.WithHttpCommand(
            path: "/consumption/pause",
            displayName: "Consumption: pause",
            commandName: "consumption-pause",
            commandOptions: new HttpCommandOptions
            {
                Description = "Stop taking messages from the queue: they pile up in RabbitMQ while the service stays up.",
                IconName = "Pause",
                IconVariant = IconVariant.Regular
            });

        project.WithHttpCommand(
            path: "/consumption/resume",
            displayName: "Consumption: resume",
            commandName: "consumption-resume",
            commandOptions: new HttpCommandOptions
            {
                Description = "Take messages from the queue again.",
                IconName = "Play",
                IconVariant = IconVariant.Regular
            });

        return project;
    }
}

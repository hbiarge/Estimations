using Acheve.Common.Shared;

// The fake external systems the application talks to. They run apart from the application,
// with their own dashboard, like systems owned by someone else. Start this AppHost before or after
// the application's AppHost; the application finds them at fixed URLs (https://localhost:5001-5004).
var builder = DistributedApplication.CreateBuilder(args);

// Every fake sends its telemetry to this collector, which sends a copy to this AppHost's dashboard and another
// to the Grafana LGTM container that the application's AppHost runs. When that AppHost isn't running,
// the collector logs export errors and the dashboard still gets everything.
builder.AddOpenTelemetryCollector("otel-collector", settings => settings.CollectorTag = "0.164.0")
    .WithConfig(Path.Combine(builder.AppHostDirectory, "otel-collector.yaml"))
    .WithEnvironment("LGTM_OTLP_ENDPOINT", "host.docker.internal:" + Constants.Telemetry.LgtmOtlpGrpcPort)
    .WithAppForwarding();

// Each fake keeps the port from its launch settings. The dashboard commands switch its chaos scenario;
// the two that call back can also lose, duplicate or delay their callbacks.
AddExternalSystem<Projects.Acheve_External_Images>(Constants.Resources.ExternalImages)
    .WithChaosCommands(Constants.Chaos.RequestScenarios);
AddExternalSystem<Projects.Acheve_External_ImageProcess>(Constants.Resources.ExternalImageProcess)
    .WithChaosCommands(Constants.Chaos.CallbackScenarios);
AddExternalSystem<Projects.Acheve_External_Estimations>(Constants.Resources.ExternalEstimations)
    .WithChaosCommands(Constants.Chaos.CallbackScenarios);
AddExternalSystem<Projects.Acheve_External_Notifications>(Constants.Resources.ExternalNotifications)
    .WithChaosCommands(Constants.Chaos.RequestScenarios);

builder.Build().Run();

IResourceBuilder<ProjectResource> AddExternalSystem<TProject>(string name) where TProject : IProjectMetadata, new() =>
    builder.AddProject<TProject>(name)
        .WithEnvironment(Constants.Telemetry.ServiceNamespaceKey.Replace(":", "__"), Constants.Telemetry.ExternalNamespace)
        .WithHttpHealthCheck("/health");

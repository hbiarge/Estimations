using System.Globalization;
using Acheve.Common.Shared;
using Aspire.Hosting.Dotnet;

// The fake external systems the application talks to. They run apart from the application,
// with their own dashboard, like systems owned by someone else. Start this AppHost before or after
// the application's AppHost; the application finds them at fixed URLs (https://external-*.dev.localhost:5001-5003 and 5020-5022; .dev.localhost names resolve to this machine and the development certificate covers them).
var builder = DistributedApplication.CreateBuilder(args);

// Every fake sends its telemetry to this collector, which sends a copy to this AppHost's dashboard and another
// to the Grafana LGTM container that the application's AppHost runs. When that AppHost isn't running,
// the collector logs export errors and the dashboard still gets everything.
builder.AddOpenTelemetryCollector("otel-collector", settings => settings.CollectorTag = "0.164.0")
    .WithConfig(Path.Combine(builder.AppHostDirectory, "otel-collector.yaml"))
    .WithEnvironment("LGTM_OTLP_ENDPOINT", "host.docker.internal:" + Constants.Telemetry.LgtmOtlpGrpcPort)
    .WithAppForwarding();

// Each fake keeps the port from its launch settings (the notification fakes get theirs from CustomerNotifications). The dashboard commands switch its chaos scenario;
// the two that call back can also lose, duplicate or delay their callbacks.
AddExternalSystem("Acheve.External.Images", Constants.Resources.ExternalImages)
    .WithChaosCommands(Constants.Chaos.RequestScenarios);
AddExternalSystem("Acheve.External.ImageProcess", Constants.Resources.ExternalImageProcess)
    .WithChaosCommands(Constants.Chaos.CallbackScenarios);
AddExternalSystem("Acheve.External.Estimations", Constants.Resources.ExternalEstimations)
    .WithChaosCommands(Constants.Chaos.CallbackScenarios);

// One notifications fake per customer: each plays that customer's callback endpoint, on its own port.
foreach (var (customer, port) in builder.Configuration.GetSection("CustomerNotifications").GetChildren().Select(x => (x.Key, int.Parse(x.Value!, CultureInfo.InvariantCulture))))
{
    AddExternalSystem("Acheve.External.Notifications", Constants.Resources.ExternalNotificationsOf(customer), port)
        .WithEnvironment(Constants.CustomerCallbacks.CustomerKey.Replace(":", "__"), customer)
        .WithChaosCommands(Constants.Chaos.RequestScenarios);
}

builder.Build().Run();

// The port is only for the fakes that run more than once; the others keep the one of their launch settings.
IResourceBuilder<DotnetProjectResource> AddExternalSystem(string project, string name, int? port = null) =>
    (port is null
        ? builder.AddDotnetProject(name, $"../../External/{project}/{project}.csproj")
        : builder.AddDotnetProject(name, $"../../External/{project}/{project}.csproj", options => options.ExcludeLaunchProfile = true)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithHttpsEndpoint(port: port, targetPort: port, isProxied: false))
        .WithEnvironment(Constants.Telemetry.ServiceNamespaceKey.Replace(":", "__"), Constants.Telemetry.ExternalNamespace)
        .WithHttpHealthCheck("/health");

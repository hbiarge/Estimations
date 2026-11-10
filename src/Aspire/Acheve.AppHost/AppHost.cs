using Acheve.Common.Shared;

var builder = DistributedApplication.CreateBuilder(args);

// Message broker used by Rebus. The dashboard links to the RabbitMQ management UI.
var messaging = builder.AddRabbitMQ(Constants.Resources.Messaging)
    .WithManagementPlugin();

// Database for the process manager's saga data and deferred messages. The volume keeps the data between runs.
var processManagerDatabase = builder.AddPostgres(Constants.Resources.Postgres)
    .WithDataVolume()
    .AddDatabase(Constants.Resources.ProcessManagerDatabase);

// Observability: Grafana with Tempo (traces), Loki (logs) and Prometheus (metrics) in one container.
// The volume keeps the telemetry between runs.
var lgtm = builder.AddContainer("lgtm", "grafana/otel-lgtm", "0.39.0")
    .WithHttpEndpoint(targetPort: 3000, name: "grafana")
    // Fixed host port so the external systems' AppHost can send its telemetry here too.
    .WithEndpoint(port: Constants.Telemetry.LgtmOtlpGrpcPort, targetPort: 4317, name: "otlp-grpc", scheme: "http")
    .WithVolume("lgtm-data", "/data")
    .WithUrlForEndpoint("grafana", url => url.DisplayText = "Grafana");

// Every application service sends its telemetry to this collector, which sends a copy to the Aspire dashboard and another to LGTM.
builder.AddOpenTelemetryCollector("otel-collector", settings => settings.CollectorTag = "0.164.0")
    .WithConfig(Path.Combine(builder.AppHostDirectory, "otel-collector.yaml"))
    .WithEnvironment("LGTM_OTLP_ENDPOINT", lgtm.GetEndpoint("otlp-grpc").Property(EndpointProperty.HostAndPort))
    .WithAppForwarding()
    .WaitFor(lgtm);

// Local folder shared by all services for Rebus data bus attachments (images and estimations).
var storagePath = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", ".data"));
Directory.CreateDirectory(storagePath);

// Key that signs the callback URLs given to the external systems. Aspire generates it on the first run
// and keeps it in the AppHost user secrets, so callbacks for cases in progress still work after a restart.
var callbackSigningKey = builder.AddParameter(
    Constants.Callbacks.SigningKeyParameter,
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);

// External systems the application talks to. The fakes run in their own AppHost (Acheve.External.AppHost),
// so here they are only URLs, from the ExternalSystems section of the settings. The application doesn't wait
// for them: like real external systems they can be down, and the health checks show it in the dashboard.
var externalImages = AddExternalSystem(Constants.Resources.ExternalImages);
var externalImageProcess = AddExternalSystem(Constants.Resources.ExternalImageProcess);
var externalEstimations = AddExternalSystem(Constants.Resources.ExternalEstimations);
var externalNotifications = AddExternalSystem(Constants.Resources.ExternalNotifications);

// Application services
var stateHolder = AddService<Projects.Acheve_Application_StateHolder>(Constants.Resources.StateHolder);

var api = AddService<Projects.Acheve_Application_Api>(Constants.Resources.Api)
    .WithCallbackSigningKey()
    .WithReference(stateHolder)
    .WaitFor(stateHolder);

AddService<Projects.Acheve_Application_ProcessManager>(Constants.Resources.ProcessManager)
    .WithReference(processManagerDatabase)
    .WaitFor(processManagerDatabase);

AddService<Projects.Acheve_Application_ImageDownloads>(Constants.Resources.ImageDownloads)
    .WithReference(externalImages);

AddService<Projects.Acheve_Application_ExternalImageProcessor>(Constants.Resources.ExternalImageProcessor)
    .WithCallbackSigningKey()
    .WithReference(externalImageProcess)
    .WithReference(api); // Sends the Api URL as the callback URL

AddService<Projects.Acheve_Application_EstimationProcessor>(Constants.Resources.EstimationProcessor)
    .WithCallbackSigningKey()
    .WithReference(externalEstimations)
    .WithReference(api); // Sends the Api URL as the callback URL

AddService<Projects.Acheve_Application_CallbackNotifier>(Constants.Resources.CallbackNotifier)
    .WithReference(externalNotifications);

builder.Build().Run();

// Every application service uses RabbitMQ and the shared storage folder.
IResourceBuilder<ProjectResource> AddService<TProject>(string name) where TProject : IProjectMetadata, new() =>
    builder.AddProject<TProject>(name)
        .WithReference(messaging)
        .WaitFor(messaging)
        .WithEnvironment(Constants.Storage.RootPathKey.Replace(":", "__"), storagePath)
        .WithEnvironment(Constants.Telemetry.ServiceNamespaceKey.Replace(":", "__"), Constants.Telemetry.ApplicationNamespace);

IResourceBuilder<ExternalServiceResource> AddExternalSystem(string name)
{
    var url = builder.Configuration[$"ExternalSystems:{name}"]
        ?? throw new InvalidOperationException($"Missing setting ExternalSystems:{name}.");

    return builder.AddExternalService(name, url)
        .WithHttpHealthCheck("/health");
}

static class CallbackSigningExtensions
{
    // The services that sign (processors) or check (Api) the callback URLs.
    public static IResourceBuilder<ProjectResource> WithCallbackSigningKey(this IResourceBuilder<ProjectResource> project)
    {
        var key = project.ApplicationBuilder.Resources
            .OfType<ParameterResource>()
            .Single(x => x.Name == Constants.Callbacks.SigningKeyParameter);

        return project.WithEnvironment(Constants.Callbacks.SigningKeyKey.Replace(":", "__"), key);
    }
}

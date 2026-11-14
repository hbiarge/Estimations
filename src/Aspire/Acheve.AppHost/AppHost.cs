using Acheve.Common.Shared;

var builder = DistributedApplication.CreateBuilder(args);

// Message broker used by Rebus. The dashboard links to the RabbitMQ management UI.
// The image also serves Prometheus metrics (queue depths, rates) on port 15692; the collector scrapes them.
var messaging = builder.AddRabbitMQ(Constants.Resources.Messaging)
    .WithManagementPlugin()
    .WithEndpoint(targetPort: 15692, name: "prometheus", scheme: "http");

// PostgreSQL server for the databases below. The volume keeps the data between runs.
var postgres = builder.AddPostgres(Constants.Resources.Postgres)
    .WithDataVolume();

// The process manager's saga data and deferred messages.
var processManagerDatabase = postgres.AddDatabase(Constants.Resources.ProcessManagerDatabase);

// The state holder's copy of every case's state, so a restart doesn't forget the cases.
var stateHolderDatabase = postgres.AddDatabase(Constants.Resources.StateHolderDatabase);

// Observability: Grafana with Tempo (traces), Loki (logs) and Prometheus (metrics) in one container.
// The volume keeps the telemetry between runs.
// The grafana folder holds our dashboards and alert rules; Grafana loads them on every start.
var grafanaPath = Path.Combine(builder.AppHostDirectory, "grafana");
var lgtm = builder.AddContainer("lgtm", "grafana/otel-lgtm", "0.39.0")
    .WithHttpEndpoint(targetPort: 3000, name: "grafana")
    // Fixed host port so the external systems' AppHost can send its telemetry here too.
    .WithEndpoint(port: Constants.Telemetry.LgtmOtlpGrpcPort, targetPort: 4317, name: "otlp-grpc", scheme: "http")
    .WithVolume("lgtm-data", "/data")
    .WithBindMount(Path.Combine(grafanaPath, "dashboards"), "/otel-lgtm/estimations-dashboards", isReadOnly: true)
    .WithBindMount(
        Path.Combine(grafanaPath, "provisioning", "dashboards.yaml"),
        "/otel-lgtm/grafana/conf/provisioning/dashboards/estimations.yaml",
        isReadOnly: true)
    .WithBindMount(
        Path.Combine(grafanaPath, "provisioning", "alerting.yaml"),
        "/otel-lgtm/grafana/conf/provisioning/alerting/estimations.yaml",
        isReadOnly: true)
    .WithUrlForEndpoint("grafana", url => url.DisplayText = "Grafana");

// The on-call Grafana: the same dashboards (except Chaos run) and alerts, fed by the collector with only what a real
// team would see: our own services, without the injected faults, the tools or the external systems' insides.
// An AI agent investigates incidents here (talk E5) with a Viewer service account token; anonymous access is off and
// the admin password is generated, so the agent can't log in any other way. Grafana keeps the password from the first
// run in its volume, so the parameter is persisted too.
// Its dashboards and alert rules are copies without any mention of chaos or the tools (OncallGrafana.cs), written
// under obj at every start: restart the AppHost to pick up changes to the originals.
var oncallGrafana = OncallGrafana.Prepare(grafanaPath, Path.Combine(builder.AppHostDirectory, "obj", "oncall-grafana"));
var oncallAdminPassword = builder.AddParameter(
    "oncall-grafana-admin-password",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);
var lgtmOncall = builder.AddContainer("lgtm-oncall", "grafana/otel-lgtm", "0.39.0")
    .WithHttpEndpoint(port: Constants.Telemetry.OncallGrafanaPort, targetPort: 3000, name: "grafana")
    .WithEndpoint(port: Constants.Telemetry.OncallOtlpGrpcPort, targetPort: 4317, name: "otlp-grpc", scheme: "http")
    .WithVolume("lgtm-oncall-data", "/data")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "false")
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", oncallAdminPassword)
    .WithBindMount(
        Path.Combine(grafanaPath, "provisioning", "dashboards.yaml"),
        "/otel-lgtm/grafana/conf/provisioning/dashboards/estimations.yaml",
        isReadOnly: true)
    .WithBindMount(oncallGrafana.Alerting, "/otel-lgtm/grafana/conf/provisioning/alerting/estimations.yaml", isReadOnly: true)
    // Every dashboard but Chaos run, which shows the injected faults: the answer to every incident.
    .WithBindMount(oncallGrafana.Dashboards, "/otel-lgtm/estimations-dashboards", isReadOnly: true)
    .WithUrlForEndpoint("grafana", url => url.DisplayText = "Grafana (on-call)");

// Every application service sends its telemetry to this collector, which sends a copy to the Aspire dashboard and another to LGTM.
// The collector also scrapes the RabbitMQ metrics and sends them to LGTM, and a filtered copy of everything to the
// on-call Grafana.
builder.AddOpenTelemetryCollector("otel-collector", settings => settings.CollectorTag = "0.164.0")
    .WithConfig(Path.Combine(builder.AppHostDirectory, "otel-collector.yaml"))
    .WithEnvironment("LGTM_OTLP_ENDPOINT", lgtm.GetEndpoint("otlp-grpc").Property(EndpointProperty.HostAndPort))
    .WithEnvironment("ONCALL_OTLP_ENDPOINT", lgtmOncall.GetEndpoint("otlp-grpc").Property(EndpointProperty.HostAndPort))
    .WithEnvironment("RABBITMQ_PROMETHEUS_ENDPOINT", messaging.GetEndpoint("prometheus").Property(EndpointProperty.HostAndPort))
    .WithAppForwarding()
    // Not WaitFor(lgtmOncall): if the on-call Grafana can't start (port 3300 taken), the main telemetry must still flow.
    // The on-call exporter queues and retries until it's up.
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
var stateHolder = AddWorkerService<Projects.Acheve_Application_StateHolder>(Constants.Resources.StateHolder)
    .WithReference(stateHolderDatabase)
    .WaitFor(stateHolderDatabase);

var api = AddService<Projects.Acheve_Application_Api>(Constants.Resources.Api)
    .WithCallbackSigningKey()
    // Chaos on our side of the external systems' callbacks (the Api's /callbacks endpoints).
    .WithChaosCommands(Constants.Chaos.RequestScenarios)
    .WithReference(stateHolder)
    .WaitFor(stateHolder);

var processManager = AddWorkerService<Projects.Acheve_Application_ProcessManager>(Constants.Resources.ProcessManager)
    .WithReference(processManagerDatabase)
    .WaitFor(processManagerDatabase)
    // The StateHolder subscribes to the state changes at startup; published before that, they would be lost.
    .WaitFor(stateHolder);

// The services that call the external systems. Their HttpClient's resilience profile (retries, hedging,
// circuit breaker, timeouts) can be switched at run time from the dashboard (Resilience: <profile>).
var imageDownloads = AddCallerService<Projects.Acheve_Application_ImageDownloads>(Constants.Resources.ImageDownloads)
    .WithReference(externalImages);

var imageProcessor = AddCallerService<Projects.Acheve_Application_ExternalImageProcessor>(Constants.Resources.ExternalImageProcessor)
    .WithCallbackSigningKey()
    .WithReference(externalImageProcess)
    .WithReference(api); // Sends the Api URL as the callback URL

var estimationProcessor = AddCallerService<Projects.Acheve_Application_EstimationProcessor>(Constants.Resources.EstimationProcessor)
    .WithCallbackSigningKey()
    .WithReference(externalEstimations)
    .WithReference(api); // Sends the Api URL as the callback URL

var callbackNotifier = AddCallerService<Projects.Acheve_Application_CallbackNotifier>(Constants.Resources.CallbackNotifier)
    .WithReference(externalNotifications);

// Tools: the traffic generator plays many clients, asking for estimations over time and following each case.
// It doesn't start with the AppHost: press Start on it in the dashboard. Rate and pattern are in its appsettings.json.
var imagesUrl = ExternalSystemUrl(Constants.Resources.ExternalImages);
builder.AddProject<Projects.Acheve_Tools_TrafficGenerator>(Constants.Resources.TrafficGenerator)
    .WithReference(api)
    .WaitFor(api)
    // The fake notifications system plays the clients' callback endpoint; the fake images system serves the images.
    .WithEnvironment("Traffic__CallbackUrl", $"{ExternalSystemUrl(Constants.Resources.ExternalNotifications)}Notification")
    .WithEnvironment("Traffic__ImageUrls__0", $"{imagesUrl}images/image01.png")
    .WithEnvironment("Traffic__ImageUrls__1", $"{imagesUrl}images/image02.jpg")
    .WithEnvironment("Traffic__BrokenImageUrl", $"{imagesUrl}images/missing.jpg")
    .WithEnvironment(Constants.Telemetry.ServiceNamespaceKey.Replace(":", "__"), Constants.Telemetry.ToolsNamespace)
    .WithExplicitStart();

// The chaos monkey runs strategies: steps (in its appsettings.json) that switch the chaos scenarios of the Api and the
// fake external systems and the resilience profiles of the services that call them. It starts idle: run a strategy
// from its commands (Strategy: <name>), ideally with the traffic generator running. When a strategy ends or is
// stopped, every service goes back to healthy, standard and running.
builder.AddProject<Projects.Acheve_Tools_ChaosMonkey>(Constants.Resources.ChaosMonkey)
    .WithReference(api)
    .WithReference(externalImages)
    .WithReference(externalImageProcess)
    .WithReference(externalEstimations)
    .WithReference(externalNotifications)
    .WithReference(imageDownloads)
    .WithReference(imageProcessor)
    .WithReference(estimationProcessor)
    .WithReference(callbackNotifier)
    .WithReference(processManager)
    .WithReference(stateHolder)
    .WithEnvironment(Constants.Telemetry.ServiceNamespaceKey.Replace(":", "__"), Constants.Telemetry.ToolsNamespace)
    .WithHttpHealthCheck("/health")
    .WithStrategyCommands();

builder.Build().Run();

// Every application service uses RabbitMQ and the shared storage folder.
IResourceBuilder<ProjectResource> AddService<TProject>(string name) where TProject : IProjectMetadata, new() =>
    builder.AddProject<TProject>(name)
        .WithReference(messaging)
        .WaitFor(messaging)
        .WithEnvironment(Constants.Storage.RootPathKey.Replace(":", "__"), storagePath)
        .WithEnvironment(Constants.Telemetry.ServiceNamespaceKey.Replace(":", "__"), Constants.Telemetry.ApplicationNamespace);

// A service with a Rebus worker: health checks and the /consumption endpoints to pause and resume taking messages.
IResourceBuilder<ProjectResource> AddWorkerService<TProject>(string name) where TProject : IProjectMetadata, new() =>
    AddService<TProject>(name)
        .WithHttpHealthCheck("/health")
        .WithConsumptionCommands();

// A worker that calls an external system: also the /resilience endpoints.
IResourceBuilder<ProjectResource> AddCallerService<TProject>(string name) where TProject : IProjectMetadata, new() =>
    AddWorkerService<TProject>(name)
        .WithResilienceCommands();

IResourceBuilder<ExternalServiceResource> AddExternalSystem(string name) =>
    builder.AddExternalService(name, ExternalSystemUrl(name))
        .WithHttpHealthCheck("/health");

// From the ExternalSystems settings, ending in a slash.
string ExternalSystemUrl(string name) =>
    builder.Configuration[$"ExternalSystems:{name}"]
        ?? throw new InvalidOperationException($"Missing setting ExternalSystems:{name}.");

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

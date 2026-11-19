// WithDisplayOrder is experimental in Aspire 17.
#pragma warning disable ASPIRERESOURCETRAITS001

using Acheve.Common.Shared;
using Aspire.Hosting.Dotnet;

var builder = DistributedApplication.CreateBuilder(args);

// Message broker used by Rebus. The dashboard links to the RabbitMQ management UI.
// The image also serves Prometheus metrics (queue depths, rates) on port 15692; the collector scrapes them.
var messaging = builder.AddRabbitMQ(Constants.Resources.Messaging)
    .WithManagementPlugin()
    .WithEndpoint(targetPort: 15692, name: "prometheus", scheme: "http")
    .WithIconName("Mail")
    .WithDisplayOrder(630);

// Identity provider for the customers: Keycloak with the "estimations" realm (src/Aspire/Acheve.AppHost/keycloak),
// which has one service-account client per customer. The Api validates the tokens it issues.
var keycloak = builder.AddKeycloak(Constants.Resources.Keycloak, port: Constants.Identity.Port)
    .WithRealmImport("./keycloak")
    .WithIconName("LockClosed")
    .WithDisplayOrder(620);

// PostgreSQL server for the databases below. The volume keeps the data between runs.
var postgres = builder.AddPostgres(Constants.Resources.Postgres)
    .WithDataVolume()
    .WithIconName("Database")
    .WithDisplayOrder(610);

// The process manager's saga data and deferred messages.
var processManagerDatabase = postgres.AddDatabase(Constants.Resources.ProcessManagerDatabase)
    .WithDisplayOrder(600);

// The state holder's copy of every case's state, so a restart doesn't forget the cases.
var stateHolderDatabase = postgres.AddDatabase(Constants.Resources.StateHolderDatabase)
    .WithDisplayOrder(599);

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
    .WithUrlForEndpoint("grafana", url => url.DisplayText = "Grafana")
    .WithIconName("DataTrending")
    .WithDisplayOrder(520);

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
    .WithUrlForEndpoint("grafana", url => url.DisplayText = "Grafana (on-call)")
    .WithIconName("Pulse")
    .WithDisplayOrder(510);

// Every application service sends its telemetry to this collector, which sends a copy to the Aspire dashboard and another to LGTM.
// The collector also scrapes the RabbitMQ metrics and sends them to LGTM, and a filtered copy of everything to the
// on-call Grafana.
builder.AddOpenTelemetryCollector("otel-collector", settings => settings.CollectorTag = "0.164.0")
    .WithConfig(Path.Combine(builder.AppHostDirectory, "otel-collector.yaml"))
    .WithEnvironment("LGTM_OTLP_ENDPOINT", lgtm.GetEndpoint("otlp-grpc").Property(EndpointProperty.HostAndPort))
    .WithEnvironment("ONCALL_OTLP_ENDPOINT", lgtmOncall.GetEndpoint("otlp-grpc").Property(EndpointProperty.HostAndPort))
    .WithEnvironment("RABBITMQ_PROMETHEUS_ENDPOINT", messaging.GetEndpoint("prometheus").Property(EndpointProperty.HostAndPort))
    .WithAppForwarding()
    .WithIconName("Filter")
    .WithDisplayOrder(500)
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

var externalSystemCount = 0;

// External systems the application talks to. The fakes run in their own AppHost (Acheve.External.AppHost),
// so here they are only URLs, from the ExternalSystems section of the settings. The application doesn't wait
// for them: like real external systems they can be down, and the health checks show it in the dashboard.
var externalImages = AddExternalSystem(Constants.Resources.ExternalImages);
var externalImageProcess = AddExternalSystem(Constants.Resources.ExternalImageProcess);
var externalEstimations = AddExternalSystem(Constants.Resources.ExternalEstimations);
var customers = builder.Configuration.GetSection("Customers:Known").GetChildren().Select(x => x.Value!).ToArray();

// One fake notification endpoint per customer.
var externalNotifications = customers
    .Select(x => AddExternalSystem(Constants.Resources.ExternalNotificationsOf(x)))
    .ToArray();

// Application services
var stateHolder = AddWorkerService("Application", "Acheve.Application.StateHolder", Constants.Resources.StateHolder)
    .WithReference(stateHolderDatabase)
    .WaitFor(stateHolderDatabase)
    .WithIconName("Archive")
    .WithDisplayOrder(890);

var api = AddService("Application", "Acheve.Application.Api", Constants.Resources.Api)
    .WithCallbackSigningKey()
    .WithIconName("Globe")
    .WithDisplayOrder(900)
    // Chaos on our side of the external systems' callbacks (the Api's /callbacks endpoints).
    .WithChaosCommands(Constants.Chaos.RequestScenarios)
    .WithEnvironment("Authentication__Authority", Constants.Identity.Authority)
    .WithEnvironment("Authentication__Audience", Constants.Identity.Audience)
    .WithReference(stateHolder)
    .WaitFor(stateHolder)
    .WaitFor(keycloak);

var processManager = AddWorkerService("Application", "Acheve.Application.ProcessManager", Constants.Resources.ProcessManager)
    .WithReference(processManagerDatabase)
    .WaitFor(processManagerDatabase)
    .WithIconName("Flowchart")
    .WithDisplayOrder(880)
    // Customers with their own metric label; any other customer is recorded as "other" (see CustomerLabels).
    .WithKnownCustomers(customers)
    // The StateHolder subscribes to the state changes at startup; published before that, they would be lost.
    .WaitFor(stateHolder);

// The services that call the external systems. Their HttpClient's resilience profile (retries, hedging,
// circuit breaker, timeouts) can be switched at run time from the dashboard (Resilience: <profile>).
var imageDownloads = AddCallerService("Application", "Acheve.Application.ImageDownloads", Constants.Resources.ImageDownloads)
    .WithReference(externalImages)
    .WithIconName("ArrowDownload")
    .WithDisplayOrder(870);

var imageProcessor = AddCallerService("Application", "Acheve.Application.ExternalImageProcessor", Constants.Resources.ExternalImageProcessor)
    .WithCallbackSigningKey()
    .WithReference(externalImageProcess)
    .WithIconName("Image")
    .WithDisplayOrder(860)
    .WithReference(api); // Sends the Api URL as the callback URL

var estimationProcessor = AddCallerService("Application", "Acheve.Application.EstimationProcessor", Constants.Resources.EstimationProcessor)
    .WithCallbackSigningKey()
    .WithReference(externalEstimations)
    .WithIconName("Calculator")
    .WithDisplayOrder(850)
    .WithReference(api); // Sends the Api URL as the callback URL

var callbackNotifier = AddCallerService("Application", "Acheve.Application.CallbackNotifier", Constants.Resources.CallbackNotifier)
    .WithReferences(externalNotifications)
    .WithIconName("Megaphone")
    .WithDisplayOrder(840);

// Tools: the traffic generator plays many customers, asking for estimations over time and following each case.
// It doesn't start with the AppHost: press Start on it in the dashboard. Rate and pattern are in its appsettings.json.
var imagesUrl = ExternalSystemUrl(Constants.Resources.ExternalImages);
builder.AddDotnetProject(Constants.Resources.TrafficGenerator, ProjectPath("Tools", "Acheve.Tools.TrafficGenerator"))
    .WithReference(api)
    .WaitFor(api)
    .WaitFor(keycloak)
    // One customer per name in Customers:Known, each with the Keycloak client of the same name. The fake
    // notifications fake of each customer plays its callback endpoint; the fake images system serves the images.
    .WithEnvironment("Traffic__TokenEndpoint", Constants.Identity.TokenEndpoint)
    .WithTrafficCustomers(
        customers,
        customer => $"{ExternalSystemUrl(Constants.Resources.ExternalNotificationsOf(customer))}Notification")
    .WithEnvironment("Traffic__ImageUrls__0", $"{imagesUrl}images/image01.png")
    .WithEnvironment("Traffic__ImageUrls__1", $"{imagesUrl}images/image02.jpg")
    .WithEnvironment("Traffic__BrokenImageUrl", $"{imagesUrl}images/missing.jpg")
    .WithEnvironment(Constants.Telemetry.ServiceNamespaceKey.Replace(":", "__"), Constants.Telemetry.ToolsNamespace)
    .WithExplicitStart()
    .WithIconName("Play")
    .WithDisplayOrder(810);

// The chaos monkey runs strategies: steps (in its appsettings.json) that switch the chaos scenarios of the Api and the
// fake external systems and the resilience profiles of the services that call them. It starts idle: run a strategy
// from its commands (Strategy: <name>), ideally with the traffic generator running. When a strategy ends or is
// stopped, every service goes back to healthy, standard and running.
builder.AddDotnetProject(Constants.Resources.ChaosMonkey, ProjectPath("Tools", "Acheve.Tools.ChaosMonkey"))
    .WithReference(api)
    .WithReference(externalImages)
    .WithReference(externalImageProcess)
    .WithReference(externalEstimations)
    .WithReferences(externalNotifications)
    .WithKnownCustomers(customers) // The notifications target switches each customer's fake
    .WithReference(imageDownloads)
    .WithReference(imageProcessor)
    .WithReference(estimationProcessor)
    .WithReference(callbackNotifier)
    .WithReference(processManager)
    .WithReference(stateHolder)
    .WithEnvironment(Constants.Telemetry.ServiceNamespaceKey.Replace(":", "__"), Constants.Telemetry.ToolsNamespace)
    .WithHttpHealthCheck("/health")
    .WithStrategyCommands()
    .WithIconName("Bug")
    .WithDisplayOrder(800);

builder.Build().Run();

// Every application service uses RabbitMQ and the shared storage folder.
// src/<group>/<project>/<project>.csproj, relative to this AppHost.
string ProjectPath(string group, string project) => $"../../{group}/{project}/{project}.csproj";

IResourceBuilder<DotnetProjectResource> AddService(string group, string project, string name) =>
    builder.AddDotnetProject(name, ProjectPath(group, project))
        .WithReference(messaging)
        .WaitFor(messaging)
        .WithEnvironment(Constants.Storage.RootPathKey.Replace(":", "__"), storagePath)
        .WithEnvironment(Constants.Telemetry.ServiceNamespaceKey.Replace(":", "__"), Constants.Telemetry.ApplicationNamespace);

// A service with a Rebus worker: health checks and the /consumption endpoints to pause and resume taking messages.
IResourceBuilder<DotnetProjectResource> AddWorkerService(string group, string project, string name) =>
    AddService(group, project, name)
        .WithHttpHealthCheck("/health")
        .WithConsumptionCommands();

// A worker that calls an external system: also the /resilience endpoints.
IResourceBuilder<DotnetProjectResource> AddCallerService(string group, string project, string name) =>
    AddWorkerService(group, project, name)
        .WithResilienceCommands();

IResourceBuilder<ExternalServiceResource> AddExternalSystem(string name) =>
    builder.AddExternalService(name, ExternalSystemUrl(name))
        .WithHttpHealthCheck("/health")
        .WithIconName("Cloud")
        .WithDisplayOrder(700 - externalSystemCount++);

// From the ExternalSystems settings, ending in a slash.
string ExternalSystemUrl(string name) =>
    builder.Configuration[$"ExternalSystems:{name}"]
        ?? throw new InvalidOperationException($"Missing setting ExternalSystems:{name}.");

internal static class ReferenceExtensions
{
    public static IResourceBuilder<T> WithReferences<T>(this IResourceBuilder<T> project, IEnumerable<IResourceBuilder<ExternalServiceResource>> externals)
        where T : IResourceWithEnvironment
    {
        foreach (var external in externals)
        {
            project.WithReference(external);
        }

        return project;
    }
}

internal static class CallbackSigningExtensions
{
    // The services that sign (processors) or check (Api) the callback URLs.
    public static IResourceBuilder<DotnetProjectResource> WithCallbackSigningKey(this IResourceBuilder<DotnetProjectResource> project)
    {
        var key = project.ApplicationBuilder.Resources
            .OfType<ParameterResource>()
            .Single(x => x.Name == Constants.Callbacks.SigningKeyParameter);

        return project.WithEnvironment(Constants.Callbacks.SigningKeyKey.Replace(":", "__"), key);
    }
}

internal static class KnownCustomersExtensions
{
    // The Customers:Known setting, as the service reads it.
    public static IResourceBuilder<DotnetProjectResource> WithKnownCustomers(
        this IResourceBuilder<DotnetProjectResource> project, IEnumerable<string> customers)
    {
        var index = 0;

        foreach (var customer in customers)
        {
            project.WithEnvironment($"Customers__Known__{index++}", customer);
        }

        return project;
    }
}

internal static class TrafficCustomersExtensions
{
    // The Traffic:Customers setting of the traffic generator: the Keycloak client of each customer has its name
    // and the secret "<name>-dev-secret" (keycloak/estimations-realm.json).
    public static IResourceBuilder<DotnetProjectResource> WithTrafficCustomers(
        this IResourceBuilder<DotnetProjectResource> project, IEnumerable<string> customers, Func<string, string> callbackUrl)
    {
        var index = 0;

        foreach (var customer in customers)
        {
            var prefix = $"Traffic__Customers__{index++}__";

            project
                .WithEnvironment(prefix + "Name", customer)
                .WithEnvironment(prefix + "ClientId", customer)
                .WithEnvironment(prefix + "ClientSecret", $"{customer}-dev-secret")
                .WithEnvironment(prefix + "CallbackUrl", callbackUrl(customer));
        }

        return project;
    }
}

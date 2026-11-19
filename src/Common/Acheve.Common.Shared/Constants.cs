namespace Acheve.Common.Shared
{
    public static class Constants
    {
        // Resource names used by the Aspire AppHost. They are also the host names used by service discovery.
        public static class Resources
        {
            public const string Messaging = "messaging";
            public const string Postgres = "postgres";
            public const string Keycloak = "keycloak";
            public const string ProcessManagerDatabase = "processmanagerdb";
            public const string StateHolderDatabase = "stateholderdb";

            public const string Api = "api";
            public const string StateHolder = "stateholder";
            public const string ProcessManager = "processmanager";
            public const string ImageDownloads = "imagedownloads";
            public const string ExternalImageProcessor = "imageprocessor";
            public const string EstimationProcessor = "estimationprocessor";
            public const string CallbackNotifier = "callbacknotifier";

            public const string ExternalImages = "external-images";
            public const string ExternalImageProcess = "external-imageprocess";
            public const string ExternalEstimations = "external-estimations";

            // The customers' notification endpoints, one fake per customer in Customers:Known. ExternalNotifications names them
            // all: it is the peer.service of the notifier's calls and the chaos target that switches every one of them.
            public const string ExternalNotifications = "external-notifications";

            /// <summary>The fake notification endpoint of a customer, for example "external-notifications-acme".</summary>
            public static string ExternalNotificationsOf(string customer) => $"{ExternalNotifications}-{customer}";

            // Tools that drive the system (not part of it).
            public const string TrafficGenerator = "trafficgenerator";
            public const string ChaosMonkey = "chaosmonkey";
        }

        public static class Telemetry
        {
            // Meter for the case-level metrics (ProcessManager and StateHolder). ServiceDefaults subscribes to it.
            public const string MeterName = "Acheve.Estimations";

            // Meter for the chaos injections (Acheve.Common.Chaos). ServiceDefaults subscribes to it.
            public const string ChaosMeterName = "Acheve.Chaos";

            // Meter for the traffic generator's customer-side view of the cases. ServiceDefaults subscribes to it.
            public const string TrafficMeterName = "Acheve.Traffic";

            // Meter for the chaos monkey's scenario switches. ServiceDefaults subscribes to it.
            public const string ChaosMonkeyMeterName = "Acheve.ChaosMonkey";

            // Meter for the resilience profile in force (Acheve.Common.Resilience). ServiceDefaults subscribes to it.
            public const string ResilienceMeterName = "Acheve.Resilience";

            // Whether each Rebus worker is consuming its queue (Acheve.Common.Consumption).
            public const string ConsumptionMeterName = "Acheve.Consumption";

            // Polly's own meter: retries, hedges, circuit breaker changes and timeouts. ServiceDefaults subscribes to it.
            public const string PollyMeterName = "Polly";

            // Read by the service defaults and added to the telemetry as service.namespace.
            public const string ServiceNamespaceKey = "Telemetry:ServiceNamespace";

            public const string ApplicationNamespace = "application";
            public const string ExternalNamespace = "external";
            public const string ToolsNamespace = "tools";

            // Fixed host port of the Grafana LGTM OTLP (gRPC) receiver, so the external systems' AppHost can reach it.
            public const int LgtmOtlpGrpcPort = 14317;

            // The on-call Grafana (lgtm-oncall): a filtered copy of our telemetry for incident investigations (talk E5).
            // Fixed host ports so the investigation skill can name the Grafana URL.
            public const int OncallGrafanaPort = 3300;
            public const int OncallOtlpGrpcPort = 14318;
        }

        public static class Queues
        {
            public const string ProcessManager = "processmanager";
            public const string ImageDownloads = "imagedownloads";
            public const string ImageProcess = "imageprocess";
            public const string ExternalEstimations = "externalestimations";
            public const string CallbackNotifications = "callbacknotifications";
            public const string StateHolder = "stateholder";

            // Messages RabbitMQ hands a worker before it acknowledges them: Rebus's default parallelism (5), instead of
            // Rebus.RabbitMq's 50. A paused or stuck worker then holds back at most 5; the rest wait (ready) in the queue.
            public const int Prefetch = 5;
        }

        // Folders inside the shared storage folder (configuration key Storage:RootPath).
        public static class Storage
        {
            public const string RootPathKey = "Storage:RootPath";

            public const string DataBus = "databus";
        }

        // The local Keycloak that issues the customers' access tokens (Aspire.Hosting.Keycloak).
        public static class Identity
        {
            public const string Realm = "estimations";

            // Fixed, so the issuer in the tokens is the same for the Api and for whoever asks for a token.
            // Aspire serves Keycloak over HTTPS with the ASP.NET Core development certificate.
            public const int Port = 8180;

            // A name under .dev.localhost: it points to this machine without any DNS setting and the ASP.NET Core
            // development certificate covers it. Keycloak (dev mode) puts the host of the request in the issuer,
            // so everyone must use this name: the Api, the tools and the requests in requests.http.
            public const string Host = "identity.dev.localhost";

            public const string Audience = "api";

            public static string Authority => $"https://{Host}:{Port}/realms/{Realm}";

            public static string TokenEndpoint => $"{Authority}/protocol/openid-connect/token";
        }

        // Key used to sign the callback URLs sent to the external systems (see CallbackSigner).
        public static class Callbacks
        {
            public const string SigningKeyParameter = "callback-signing-key";
            public const string SigningKeyKey = "Callbacks:SigningKey";
        }

        // Keys that sign the notifications sent to the customers' callback URLs, one per customer
        // (see NotificationSigner and CustomerNotificationSigners).
        public static class CustomerCallbacks
        {
            public const string SigningKeysSection = "CustomerCallbacks:SigningKeys";

            // Only for a fake customer endpoint: the customer it plays, to pick its key.
            public const string CustomerKey = "CustomerCallbacks:Customer";
        }

        // Tables Rebus creates in the process manager database.
        public static class Tables
        {
            public const string SagaData = "saga_data";
            public const string SagaIndex = "saga_index";
            public const string Timeouts = "timeouts";

            // Table the state holder creates in its database.
            public const string CaseStates = "case_states";
        }

        // Chaos scenarios the services can switch to at run time (Acheve.Common.Chaos).
        public static class Chaos
        {
            public const string ConfigurationSection = "Chaos";

            public const string Healthy = "healthy";
            public const string Flaky = "flaky";
            public const string Slow = "slow";
            public const string Down = "down";
            public const string LostCallbacks = "lost-callbacks";
            public const string DuplicateCallbacks = "duplicate-callbacks";

            // Scenarios every service with chaos supports.
            public static readonly string[] RequestScenarios = [Healthy, Flaky, Slow, Down];

            // Also for the external systems that call back (image analysis and estimations).
            public static readonly string[] CallbackScenarios = [Healthy, Flaky, Slow, Down, LostCallbacks, DuplicateCallbacks];
        }

        // Resilience profiles the services' HTTP clients can switch to at run time (Acheve.Common.Resilience).
        public static class Resilience
        {
            public const string ConfigurationSection = "Resilience";

            public const string None = "none";
            public const string RetryOnly = "retry-only";
            public const string Standard = "standard";
            public const string FailFast = "fail-fast";
            public const string Hedging = "hedging";

            public static readonly string[] Profiles = [None, RetryOnly, Standard, FailFast, Hedging];
        }

        // Whether a Rebus worker takes messages from its queue; switched at run time (Acheve.Common.Consumption).
        public static class Consumption
        {
            public const string Paused = "paused";
            public const string Running = "running";

            public static readonly string[] States = [Paused, Running];

            // The services with a Rebus worker. The Api only sends, so it has nothing to pause.
            public static readonly string[] Workers =
            [
                Resources.ImageDownloads,
                Resources.ExternalImageProcessor,
                Resources.EstimationProcessor,
                Resources.CallbackNotifier,
                Resources.ProcessManager,
                Resources.StateHolder
            ];
        }

        // Strategies the chaos monkey can run (Acheve.Tools.ChaosMonkey). Their steps are in its settings;
        // the names are here because the AppHost adds a dashboard command for each one.
        public static class ChaosMonkey
        {
            public const string Retries = "retries";
            public const string RetryStorm = "retry-storm";
            public const string Hedging = "hedging";
            public const string Timeouts = "timeouts";
            public const string Callbacks = "callbacks";
            public const string Backlog = "backlog";
            public const string BadDay = "bad-day";
            public const string Incident = "incident";
            public const string Tour = "tour";
            public const string Random = "random";

            public static readonly string[] Strategies = [Retries, RetryStorm, Hedging, Timeouts, Callbacks, Backlog, BadDay, Incident, Tour, Random];
        }
    }
}

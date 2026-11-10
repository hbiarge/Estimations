namespace Acheve.Common.Shared
{
    public static class Constants
    {
        // Resource names used by the Aspire AppHost. They are also the host names used by service discovery.
        public static class Resources
        {
            public const string Messaging = "messaging";
            public const string Postgres = "postgres";
            public const string ProcessManagerDatabase = "processmanagerdb";

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
            public const string ExternalNotifications = "external-notifications";

            // Tools that drive the system (not part of it).
            public const string TrafficGenerator = "trafficgenerator";
        }

        public static class Telemetry
        {
            // Meter for the case-level metrics (ProcessManager and StateHolder). ServiceDefaults subscribes to it.
            public const string MeterName = "Acheve.Estimations";

            // Meter for the chaos injections (Acheve.Common.Chaos). ServiceDefaults subscribes to it.
            public const string ChaosMeterName = "Acheve.Chaos";

            // Meter for the traffic generator's client-side view of the cases. ServiceDefaults subscribes to it.
            public const string TrafficMeterName = "Acheve.Traffic";

            // Read by the service defaults and added to the telemetry as service.namespace.
            public const string ServiceNamespaceKey = "Telemetry:ServiceNamespace";

            public const string ApplicationNamespace = "application";
            public const string ExternalNamespace = "external";
            public const string ToolsNamespace = "tools";

            // Fixed host port of the Grafana LGTM OTLP (gRPC) receiver, so the external systems' AppHost can reach it.
            public const int LgtmOtlpGrpcPort = 14317;
        }

        public static class Queues
        {
            public const string ProcessManager = "processmanager";
            public const string ImageDownloads = "imagedownloads";
            public const string ImageProcess = "imageprocess";
            public const string ExternalEstimations = "externalestimations";
            public const string CallbackNotifications = "callbacknotifications";
            public const string StateHolder = "stateholder";
        }

        // Folders inside the shared storage folder (configuration key Storage:RootPath).
        public static class Storage
        {
            public const string RootPathKey = "Storage:RootPath";

            public const string DataBus = "databus";
        }

        // Key used to sign the callback URLs sent to the external systems (see CallbackSigner).
        public static class Callbacks
        {
            public const string SigningKeyParameter = "callback-signing-key";
            public const string SigningKeyKey = "Callbacks:SigningKey";
        }

        // Tables Rebus creates in the process manager database.
        public static class Tables
        {
            public const string SagaData = "saga_data";
            public const string SagaIndex = "saga_index";
            public const string Timeouts = "timeouts";
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
    }
}

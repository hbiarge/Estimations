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

        public static class FailureThresholds
        {
            // Services with thresholds near 0 are flaky and have more probabilities to throw exceptions
            // Services with thresholds near 1 are stable and have fewer probabilities to throw exceptions

            public const double ApplicationReceivingExternalImages = 1;
            public const double ApplicationReceivingExternalEstimations = 1;

            public const double ExternalImagesProcessing = 1;
            public const double ExternalEstimationsProcessing = 1;
            public const double ExternalNotificationsProcessing = 1;
        }
    }
}

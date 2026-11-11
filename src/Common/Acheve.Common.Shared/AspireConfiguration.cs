using Microsoft.Extensions.Configuration;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// Reads the settings the Aspire AppHost passes to each service.
    /// </summary>
    public static class AspireConfiguration
    {
        public static string GetMessagingConnectionString(this IConfiguration configuration) =>
            configuration.GetRequiredConnectionString(Constants.Resources.Messaging);

        public static string GetRequiredConnectionString(this IConfiguration configuration, string name) =>
            configuration.GetConnectionString(name)
            ?? throw Missing($"ConnectionStrings:{name}");

        /// <summary>
        /// Returns a folder inside the storage folder shared by all services.
        /// </summary>
        public static string GetStoragePath(this IConfiguration configuration, string folder)
        {
            var root = configuration[Constants.Storage.RootPathKey] ?? throw Missing(Constants.Storage.RootPathKey);

            return Path.Combine(root, folder);
        }

        /// <summary>
        /// Returns the public base URL of another service, for example to send it as a callback URL.
        /// Use service discovery (https://resource-name) for HTTP calls made by this service instead.
        /// </summary>
        public static string GetServiceUrl(this IConfiguration configuration, string resourceName) =>
            configuration[$"services:{resourceName}:https:0"]
            ?? configuration[$"services:{resourceName}:http:0"]
            ?? throw Missing($"services:{resourceName}");

        public static string GetCallbackSigningKey(this IConfiguration configuration) =>
            configuration[Constants.Callbacks.SigningKeyKey] ?? throw Missing(Constants.Callbacks.SigningKeyKey);

        /// <summary>
        /// The signer for the notifications sent to the client, or null when no key is set.
        /// </summary>
        public static NotificationSigner? GetNotificationSigner(this IConfiguration configuration) =>
            configuration[Constants.ClientCallbacks.SigningKeyKey] is { Length: > 0 } key ? new NotificationSigner(key) : null;

        private static InvalidOperationException Missing(string key) =>
            new($"The setting '{key}' is missing. Start the system through the Aspire AppHost (src/Aspire/Acheve.AppHost).");
    }
}

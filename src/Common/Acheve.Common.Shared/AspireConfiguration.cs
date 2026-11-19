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
        /// The signing key of each customer (CustomerCallbacks:SigningKeys:{customerId}).
        /// With <paramref name="requireKeys"/> (anywhere but local development) at least one key must be set,
        /// and a customer without a key is not notified; without it, that customer is notified unsigned.
        /// </summary>
        public static CustomerNotificationSigners GetNotificationSigners(this IConfiguration configuration, bool requireKeys)
        {
            var keys = configuration.GetSection(Constants.CustomerCallbacks.SigningKeysSection)
                .GetChildren()
                .Select(x => new KeyValuePair<string, string?>(x.Key, x.Value));

            var signers = new CustomerNotificationSigners(keys, allowUnsigned: !requireKeys);

            if (requireKeys && signers.Count == 0)
            {
                throw Missing($"{Constants.CustomerCallbacks.SigningKeysSection}:<customer id>");
            }

            return signers;
        }

        /// <summary>
        /// The signer a fake customer endpoint checks with: the key of the customer it plays
        /// (CustomerCallbacks:Customer), or null when it has none.
        /// </summary>
        public static NotificationSigner? GetNotificationSigner(this IConfiguration configuration) =>
            configuration.GetNotificationSigners(requireKeys: false).For(configuration[Constants.CustomerCallbacks.CustomerKey]);

        private static InvalidOperationException Missing(string key) =>
            new($"The setting '{key}' is missing. Start the system through the Aspire AppHost (src/Aspire/Acheve.AppHost).");
    }
}

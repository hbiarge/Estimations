namespace Acheve.Common.Shared
{
    /// <summary>
    /// The key that signs the notifications of each customer. Every customer has their own key, so a customer who
    /// knows theirs can't forge a notification that looks like it was sent to another customer.
    /// </summary>
    public sealed class CustomerNotificationSigners
    {
        private readonly Dictionary<string, NotificationSigner> _signers;

        /// <param name="keys">The key of each customer, by customer id. Blank keys are ignored.</param>
        /// <param name="allowUnsigned">
        /// Whether a customer without a key can be notified, unsigned. For local development only:
        /// elsewhere a notification nobody can check must not be sent.
        /// </param>
        public CustomerNotificationSigners(
            IEnumerable<KeyValuePair<string, string?>> keys, bool allowUnsigned, TimeProvider? timeProvider = null)
        {
            _signers = keys
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .ToDictionary(x => x.Key, x => new NotificationSigner(x.Value!, timeProvider), StringComparer.Ordinal);

            AllowUnsigned = allowUnsigned;
        }

        public bool AllowUnsigned { get; }

        /// <summary>How many customers have a key.</summary>
        public int Count => _signers.Count;

        /// <summary>The customer's signer, or null when the customer has no key.</summary>
        public NotificationSigner? For(string? customerId) =>
            customerId is not null && _signers.TryGetValue(customerId, out var signer) ? signer : null;
    }
}

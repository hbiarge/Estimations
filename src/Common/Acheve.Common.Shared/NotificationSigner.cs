using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// Signs the notifications sent to the client's callback URL, so the client can check that they come from us
    /// and are recent. The signature is an HMAC-SHA256 over "{timestamp}.{body}" with a key both sides know,
    /// sent in the <see cref="SignatureHeader"/> header next to the <see cref="TimestampHeader"/> header.
    /// </summary>
    public sealed class NotificationSigner
    {
        public const string SignatureHeader = "X-Estimations-Signature";
        public const string TimestampHeader = "X-Estimations-Timestamp";
        public const string SignaturePrefix = "sha256=";

        // How old a notification can be when the client checks it.
        public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

        private readonly byte[] _key;
        private readonly TimeProvider _timeProvider;

        public NotificationSigner(string key, TimeProvider? timeProvider = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("The notification signing key can't be empty.", nameof(key));
            }

            _key = Encoding.UTF8.GetBytes(key);
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>The values of the timestamp and signature headers for this body.</summary>
        public (string Timestamp, string Signature) Sign(string body)
        {
            var timestamp = _timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

            return (timestamp, SignaturePrefix + Hash(timestamp, body));
        }

        public bool IsValid(string body, string? timestamp, string? signature, TimeSpan? tolerance = null)
        {
            if (string.IsNullOrEmpty(signature)
                || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var sentAt))
            {
                return false;
            }

            var signed = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(SignaturePrefix + Hash(timestamp!, body)),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));

            var age = _timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(sentAt);

            return signed && age.Duration() <= (tolerance ?? DefaultTolerance);
        }

        private string Hash(string timestamp, string body) =>
            Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{timestamp}.{body}")));
    }
}

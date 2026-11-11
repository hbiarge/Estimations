using System.Security.Cryptography;
using System.Text;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// The Idempotency-Key header: the same key means the same request, sent again.
    /// The Api accepts it on POST /estimations, and the application sends it on every POST to an external
    /// system and to the client, because HTTP retries and redelivered messages can repeat those calls.
    /// </summary>
    public static class Idempotency
    {
        public const string HeaderName = "Idempotency-Key";

        public const int MaxKeyLength = 200;

        /// <summary>
        /// The case number for a client's idempotency key: always the same for the same client and key,
        /// so a repeated request names the case the first one started and the saga ignores it.
        /// </summary>
        public static Guid CaseNumberFor(string clientId, string idempotencyKey)
        {
            // The length prefix keeps ("ab", "c") and ("a", "bc") apart.
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{clientId.Length}:{clientId}:{idempotencyKey}"));

            return new Guid(hash.AsSpan(0, 16));
        }
    }
}

using System.Collections.Concurrent;

namespace Acheve.External.Shared
{
    /// <summary>
    /// Remembers the Idempotency-Key of the requests a fake accepted, so the same request sent again
    /// (an HTTP retry, a redelivered message) is accepted without doing the work twice.
    /// </summary>
    public sealed class SeenRequests(TimeProvider timeProvider)
    {
        // Longer than the callers' retries; short enough to keep the memory small.
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new();

        /// <summary>
        /// True when the request is new and must be processed; false when a request with the same key
        /// was accepted in the last minutes. A request without a key is always new.
        /// </summary>
        public bool IsNew(string? idempotencyKey)
        {
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                return true;
            }

            var now = timeProvider.GetUtcNow();

            foreach (var (key, seenAt) in _seen)
            {
                if (now - seenAt > Lifetime)
                {
                    _seen.TryRemove(new KeyValuePair<string, DateTimeOffset>(key, seenAt));
                }
            }

            return _seen.TryAdd(idempotencyKey, now);
        }
    }
}

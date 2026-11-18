using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// Puts the customer on the current span and in every log written inside the returned scope.
    /// Spans and logs take any number of customers; metrics don't (see <see cref="CustomerLabels"/>).
    /// </summary>
    public static class CustomerTelemetry
    {
        public const string Tag = "estimation.customer_id";

        /// <summary>Null, doing nothing, when there is no customer id.</summary>
        public static IDisposable? BeginScope(ILogger logger, string? customerId)
        {
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return null;
            }

            Activity.Current?.SetTag(Tag, customerId);

            return logger.BeginScope(new[] { new KeyValuePair<string, object?>(Tag, customerId) });
        }
    }
}

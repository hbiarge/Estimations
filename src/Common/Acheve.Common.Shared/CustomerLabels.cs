using Microsoft.Extensions.Configuration;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// The value of the customer label on metrics. A label value is a time series, so only the customers in the
    /// Customers:Known setting keep their name; every other customer is <see cref="Other"/>.
    /// </summary>
    public sealed class CustomerLabels
    {
        public const string Other = "other";

        private readonly HashSet<string> _known;

        public CustomerLabels(IEnumerable<string> known)
        {
            _known = new HashSet<string>(known, StringComparer.Ordinal);
            Known = _known.ToArray();
            All = [.. Known, Other];
        }

        public IReadOnlyCollection<string> Known { get; }

        /// <summary>Every value <see cref="For"/> can return.</summary>
        public IReadOnlyCollection<string> All { get; }

        public string For(string? customerId) =>
            customerId is not null && _known.Contains(customerId) ? customerId : Other;

        public static CustomerLabels FromConfiguration(IConfiguration configuration) =>
            new(configuration.GetSection("Customers:Known").GetChildren().Select(x => x.Value).OfType<string>());
    }
}

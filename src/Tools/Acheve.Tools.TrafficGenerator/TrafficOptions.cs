using System.ComponentModel.DataAnnotations;

namespace Acheve.Tools.TrafficGenerator
{
    public enum TrafficPattern
    {
        Steady,
        Wave,
        Bursts
    }

    /// <summary>The Traffic configuration section.</summary>
    public sealed class TrafficOptions
    {
        public const string Section = "Traffic";

        [Range(0, 10_000)]
        public double CasesPerMinute { get; set; } = 6;

        public TrafficPattern Pattern { get; set; } = TrafficPattern.Steady;

        public TimeSpan WavePeriod { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan BurstEvery { get; set; } = TimeSpan.FromMinutes(5);

        [Range(0, 10_000)]
        public int BurstSize { get; set; } = 20;

        [Range(1, 20)]
        public int MaxImagesPerCase { get; set; } = 3;

        [Range(0, 1)]
        public double BrokenImageRate { get; set; } = 0.05;

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

        public TimeSpan CaseTimeout { get; set; } = TimeSpan.FromMinutes(15);

        [Range(1, 100_000)]
        public int MaxCasesInFlight { get; set; } = 300;

        /// <summary>The customers the generator plays. Each case is started for one of them, in proportion to its weight.</summary>
        [MinLength(1)]
        public CustomerOptions[] Customers { get; set; } = [];

        [MinLength(1)]
        public string[] ImageUrls { get; set; } = [];

        public string? BrokenImageUrl { get; set; }

        /// <summary>The identity provider's token endpoint (Keycloak's realm), where each customer gets its token.</summary>
        [Required]
        public string TokenEndpoint { get; set; } = "";
    }

    /// <summary>A customer the generator plays: its identity and where the Api notifies the result.</summary>
    public sealed class CustomerOptions
    {
        /// <summary>The customer's name, as it shows in the telemetry (the customer_id claim of its tokens).</summary>
        [Required]
        public string Name { get; set; } = "";

        /// <summary>The OAuth client of the customer at the identity provider.</summary>
        [Required]
        public string ClientId { get; set; } = "";

        [Required]
        public string ClientSecret { get; set; } = "";

        /// <summary>Where the Api notifies the result: the fake notifications system plays the customer's endpoint.</summary>
        [Required]
        public string CallbackUrl { get; set; } = "";

        /// <summary>Share of the cases started for this customer; 0 means none.</summary>
        [Range(0, 1000)]
        public double Weight { get; set; } = 1;
    }
}

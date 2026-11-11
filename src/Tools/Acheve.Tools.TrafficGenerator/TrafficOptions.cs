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

        /// <summary>Where the Api notifies the result: the fake notifications system plays the client's endpoint.</summary>
        [Required]
        public string CallbackUrl { get; set; } = "";

        [MinLength(1)]
        public string[] ImageUrls { get; set; } = [];

        public string? BrokenImageUrl { get; set; }

        public TokenOptions Token { get; set; } = new();
    }

    public sealed class TokenOptions
    {
        [Required]
        public string Authority { get; set; } = "";

        [Required]
        public string ClientId { get; set; } = "";

        [Required]
        public string ClientSecret { get; set; } = "";

        [Required]
        public string Scope { get; set; } = "";
    }
}

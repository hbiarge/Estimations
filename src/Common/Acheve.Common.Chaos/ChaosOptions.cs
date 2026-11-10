namespace Acheve.Common.Chaos
{
    /// <summary>Settings from the Chaos configuration section that don't change at run time.</summary>
    public sealed class ChaosOptions
    {
        /// <summary>Scenario in force when the service starts. Defaults to healthy.</summary>
        public string? Scenario { get; set; }

        /// <summary>Path prefixes the chaos applies to. Empty means every path (except health checks and /chaos).</summary>
        public string[] Paths { get; set; } = [];
    }
}

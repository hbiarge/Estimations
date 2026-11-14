using Acheve.Common.Shared;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>The services with a Rebus worker that can pause taking messages (Acheve.Common.Consumption).</summary>
    internal static class ConsumptionTargets
    {
        public static IReadOnlyList<string> Names { get; } = Constants.Consumption.Workers;

        public static bool Supports(string worker, string state) =>
            Names.Contains(worker) && Constants.Consumption.States.Contains(state);
    }
}

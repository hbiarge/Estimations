using Acheve.Common.Shared;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>The services with chaos (Acheve.Common.Chaos) and the scenarios each one supports.</summary>
    internal static class ChaosTargets
    {
        public static IReadOnlyDictionary<string, string[]> Scenarios { get; } = new Dictionary<string, string[]>
        {
            [Constants.Resources.Api] = Constants.Chaos.RequestScenarios,
            [Constants.Resources.ExternalImages] = Constants.Chaos.RequestScenarios,
            [Constants.Resources.ExternalImageProcess] = Constants.Chaos.CallbackScenarios,
            [Constants.Resources.ExternalEstimations] = Constants.Chaos.CallbackScenarios,
            [Constants.Resources.ExternalNotifications] = Constants.Chaos.RequestScenarios
        };

        public static IEnumerable<string> Names => Scenarios.Keys;

        public static bool Supports(string target, string scenario) =>
            Scenarios.TryGetValue(target, out var scenarios) && scenarios.Contains(scenario);
    }
}

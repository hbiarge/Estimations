using Acheve.Common.Shared;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>The services that call the external systems (Acheve.Common.Resilience); each one supports every profile.</summary>
    internal static class ResilienceTargets
    {
        public static IReadOnlyList<string> Names { get; } =
        [
            Constants.Resources.ImageDownloads,
            Constants.Resources.ExternalImageProcessor,
            Constants.Resources.EstimationProcessor,
            Constants.Resources.CallbackNotifier
        ];

        public static bool Supports(string caller, string profile) =>
            Names.Contains(caller) && Constants.Resilience.Profiles.Contains(profile);
    }
}

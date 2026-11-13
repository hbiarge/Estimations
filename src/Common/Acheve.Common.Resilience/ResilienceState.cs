using System.Diagnostics.Metrics;
using Acheve.Common.Shared;
using Microsoft.Extensions.Logging;

namespace Acheve.Common.Resilience
{
    /// <summary>
    /// The resilience settings in force in this service. They change at run time through the /resilience endpoints,
    /// and every switchable HttpClient picks them up on its next request.
    /// </summary>
    public sealed partial class ResilienceState
    {
        // Name of the settings set through PUT /resilience.
        public const string CustomProfile = "custom";

        private static readonly string[] ProfileNames = [.. ResilienceSettings.Profiles.Keys, CustomProfile];

        private readonly ILogger<ResilienceState> _logger;
        private ResilienceSettings _current;

        public ResilienceState(ResilienceSettings initial, IMeterFactory meterFactory, ILogger<ResilienceState> logger)
        {
            _current = initial;
            _logger = logger;

            var meter = meterFactory.Create(Constants.Telemetry.ResilienceMeterName);

            // 1 for the profile in force and 0 for the rest, so dashboards can show when each one was on.
            meter.CreateObservableGauge(
                "resilience.profile.active",
                () => ProfileNames.Select(name => new Measurement<int>(
                    name == Current.Profile ? 1 : 0,
                    new KeyValuePair<string, object?>("resilience.profile", name))),
                unit: "{profile}",
                description: "1 for the resilience profile in force, 0 for the others.");
        }

        public ResilienceSettings Current => Volatile.Read(ref _current);

        public void Set(ResilienceSettings settings)
        {
            var previous = Interlocked.Exchange(ref _current, settings);

            // Only real changes are worth a log line (the Chaos run dashboard turns these lines into annotations).
            if (previous != settings)
            {
                LogProfileChanged(previous.Profile, settings.Profile, settings);
            }
        }

        public bool TrySetProfile(string name)
        {
            if (!ResilienceSettings.Profiles.TryGetValue(name, out var settings))
            {
                return false;
            }

            Set(settings);
            return true;
        }

        [LoggerMessage(LogLevel.Warning, "Resilience profile changed from {previous} to {profile}: {settings}")]
        private partial void LogProfileChanged(string previous, string profile, ResilienceSettings settings);
    }
}

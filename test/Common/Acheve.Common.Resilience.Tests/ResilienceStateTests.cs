using Acheve.Common.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.Metrics;

namespace Acheve.Common.Resilience.Tests
{
    public class ResilienceStateTests
    {
        private static ResilienceState CreateState(ResilienceSettings initial, out IMeterFactory meterFactory)
        {
            meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
            return new ResilienceState(initial, meterFactory, NullLogger<ResilienceState>.Instance);
        }

        [Fact]
        public void Every_profile_constant_has_valid_settings()
        {
            ResilienceSettings.Profiles.Keys.ShouldBe(Constants.Resilience.Profiles, ignoreOrder: true);

            foreach (var (name, settings) in ResilienceSettings.Profiles)
            {
                settings.Profile.ShouldBe(name);
                ResiliencePipelines.Validate(settings).ShouldBeNull();
            }
        }

        [Fact]
        public void Switches_to_a_known_profile()
        {
            var state = CreateState(ResilienceSettings.Profiles[Constants.Resilience.Standard], out _);

            state.TrySetProfile(Constants.Resilience.None).ShouldBeTrue();

            state.Current.ShouldBe(ResilienceSettings.Profiles[Constants.Resilience.None]);
        }

        [Fact]
        public void Keeps_the_profile_when_the_name_is_unknown()
        {
            var standard = ResilienceSettings.Profiles[Constants.Resilience.Standard];
            var state = CreateState(standard, out _);

            state.TrySetProfile("bubble-wrap").ShouldBeFalse();

            state.Current.ShouldBe(standard);
        }

        [Fact]
        public void Reports_the_active_profile()
        {
            var state = CreateState(ResilienceSettings.Profiles[Constants.Resilience.Standard], out var meterFactory);
            using var active = new MetricCollector<int>(meterFactory, Constants.Telemetry.ResilienceMeterName, "resilience.profile.active");

            state.TrySetProfile(Constants.Resilience.Hedging);
            active.RecordObservableInstruments();

            var measurements = active.GetMeasurementSnapshot();
            measurements.Single(m => (string)m.Tags["resilience.profile"]! == Constants.Resilience.Hedging).Value.ShouldBe(1);
            measurements.Where(m => (string)m.Tags["resilience.profile"]! != Constants.Resilience.Hedging).ShouldAllBe(m => m.Value == 0);
        }

        [Fact]
        public void Rejects_retry_and_hedging_together()
        {
            var settings = new ResilienceSettings { Retry = new RetrySettings(), Hedging = new HedgingSettings() };

            ResiliencePipelines.Validate(settings).ShouldNotBeNull();
        }

        [Fact]
        public void Rejects_a_concurrency_limit_of_zero()
        {
            ResiliencePipelines.Validate(new ResilienceSettings { ConcurrencyLimit = 0 }).ShouldNotBeNull();
        }

        [Fact]
        public void Rejects_settings_Polly_refuses()
        {
            var settings = new ResilienceSettings { CircuitBreaker = new CircuitBreakerSettings { MinimumThroughput = 1 } };

            ResiliencePipelines.Validate(settings).ShouldNotBeNull();
        }
    }
}

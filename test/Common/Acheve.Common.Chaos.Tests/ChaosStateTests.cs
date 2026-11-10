using Acheve.Common.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;

namespace Acheve.Common.Chaos.Tests
{
    public class ChaosStateTests
    {
        private static ChaosState CreateState(ChaosSettings initial, double random, out IMeterFactory meterFactory)
        {
            meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
            return new ChaosState(initial, meterFactory, NullLogger<ChaosState>.Instance, () => random);
        }

        [Fact]
        public void Switches_to_a_known_scenario()
        {
            var state = CreateState(ChaosSettings.Healthy, 0, out _);

            state.TrySetScenario(Constants.Chaos.Down).ShouldBeTrue();

            state.Current.Scenario.ShouldBe(Constants.Chaos.Down);
            state.Current.FaultRate.ShouldBe(1);
        }

        [Fact]
        public void Keeps_the_scenario_when_the_name_is_unknown()
        {
            var state = CreateState(ChaosSettings.Healthy, 0, out _);

            state.TrySetScenario("meteor-strike").ShouldBeFalse();

            state.Current.ShouldBe(ChaosSettings.Healthy);
        }

        [Fact]
        public void Every_scenario_constant_has_settings()
        {
            ChaosSettings.Scenarios.Keys.ShouldBe(Constants.Chaos.CallbackScenarios, ignoreOrder: true);

            foreach (var (name, settings) in ChaosSettings.Scenarios)
            {
                settings.Scenario.ShouldBe(name);
            }
        }

        [Fact]
        public void Healthy_callbacks_go_out_once_and_on_time()
        {
            var state = CreateState(ChaosSettings.Healthy, 0, out _);

            state.PlanCallback().ShouldBe(new CallbackPlan(Drop: false, Duplicate: false, TimeSpan.Zero));
        }

        [Fact]
        public void Drops_the_callback_when_the_roll_is_under_the_drop_rate()
        {
            var state = CreateState(ChaosSettings.Scenarios[Constants.Chaos.LostCallbacks], 0.49, out var meterFactory);
            using var injections = new MetricCollector<long>(meterFactory, Constants.Telemetry.ChaosMeterName, "chaos.injections");

            state.PlanCallback().Drop.ShouldBeTrue();

            var measurement = injections.GetMeasurementSnapshot().ShouldHaveSingleItem();
            measurement.Tags["chaos.kind"].ShouldBe("callback_drop");
            measurement.Tags["chaos.scenario"].ShouldBe(Constants.Chaos.LostCallbacks);
        }

        [Fact]
        public void Sends_the_callback_when_the_roll_is_over_the_drop_rate()
        {
            var state = CreateState(ChaosSettings.Scenarios[Constants.Chaos.LostCallbacks], 0.5, out _);

            state.PlanCallback().Drop.ShouldBeFalse();
        }

        [Fact]
        public void Duplicates_the_callback_when_the_roll_is_under_the_duplicate_rate()
        {
            var state = CreateState(ChaosSettings.Scenarios[Constants.Chaos.DuplicateCallbacks], 0.1, out _);

            state.PlanCallback().ShouldBe(new CallbackPlan(Drop: false, Duplicate: true, TimeSpan.Zero));
        }

        [Fact]
        public void Slow_delays_every_callback()
        {
            var state = CreateState(ChaosSettings.Scenarios[Constants.Chaos.Slow], 0.99, out _);

            state.PlanCallback().Delay.ShouldBe(TimeSpan.FromSeconds(20));
        }

        [Fact]
        public void Reports_1_for_the_scenario_in_force_and_0_for_the_others()
        {
            var state = CreateState(ChaosSettings.Healthy, 0, out var meterFactory);
            using var active = new MetricCollector<int>(meterFactory, Constants.Telemetry.ChaosMeterName, "chaos.scenario.active");

            state.TrySetScenario(Constants.Chaos.Down);
            active.RecordObservableInstruments();

            var values = active.GetMeasurementSnapshot()
                .ToDictionary(m => (string)m.Tags["chaos.scenario"]!, m => m.Value);

            values[Constants.Chaos.Down].ShouldBe(1);
            values.Where(v => v.Key != Constants.Chaos.Down).ShouldAllBe(v => v.Value == 0);
            values.Keys.ShouldContain(ChaosState.CustomScenario);
        }

        [Fact]
        public void Logs_a_scenario_change_only_when_the_settings_change()
        {
            var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
            var logger = new FakeLogger<ChaosState>();
            var state = new ChaosState(ChaosSettings.Healthy, meterFactory, logger, () => 0);

            state.TrySetScenario(Constants.Chaos.Healthy);
            state.TrySetScenario(Constants.Chaos.Down);
            state.TrySetScenario(Constants.Chaos.Down);
            state.TrySetScenario(Constants.Chaos.Healthy);

            logger.Collector.GetSnapshot()
                .Where(r => r.Level == LogLevel.Warning)
                .Select(r => r.Message)
                .ShouldSatisfyAllConditions(
                    messages => messages.Count().ShouldBe(2),
                    messages => messages.First().ShouldStartWith("Chaos scenario changed from healthy to down"),
                    messages => messages.Last().ShouldStartWith("Chaos scenario changed from down to healthy"));
        }
    }
}

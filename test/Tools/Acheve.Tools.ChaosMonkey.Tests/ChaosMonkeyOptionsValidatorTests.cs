using Acheve.Common.Shared;
using Microsoft.Extensions.Configuration;

namespace Acheve.Tools.ChaosMonkey.Tests
{
    public class ChaosMonkeyOptionsValidatorTests
    {
        private static readonly ChaosMonkeyOptionsValidator Validator = new();

        private static ChaosMonkeyOptions OneStep(string target, string scenario, TimeSpan? duration = null) => new()
        {
            Timeline = [new() { Duration = duration ?? TimeSpan.FromMinutes(1), Scenarios = { [target] = scenario } }]
        };

        [Fact]
        public void The_shipped_settings_are_valid()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "chaosmonkey.appsettings.json"))
                .Build();
            var options = configuration.GetSection(ChaosMonkeyOptions.Section).Get<ChaosMonkeyOptions>()!;

            var result = Validator.Validate(null, options);

            result.Succeeded.ShouldBeTrue(result.FailureMessage);
            options.Timeline.Count.ShouldBeGreaterThan(1);
        }

        [Fact]
        public void An_unknown_target_fails()
        {
            Validator.Validate(null, OneStep("database", Constants.Chaos.Down)).FailureMessage.ShouldNotBeNull().ShouldContain("unknown target 'database'");
        }

        [Fact]
        public void A_scenario_the_target_does_not_support_fails()
        {
            Validator.Validate(null, OneStep(Constants.Resources.Api, Constants.Chaos.LostCallbacks))
                .FailureMessage.ShouldNotBeNull().ShouldContain("api doesn't support 'lost-callbacks'");
        }

        [Fact]
        public void A_step_without_duration_fails()
        {
            Validator.Validate(null, OneStep(Constants.Resources.Api, Constants.Chaos.Down, TimeSpan.Zero))
                .FailureMessage.ShouldNotBeNull().ShouldContain("Duration must be positive");
        }

        [Fact]
        public void An_empty_timeline_fails()
        {
            Validator.Validate(null, new ChaosMonkeyOptions()).FailureMessage.ShouldNotBeNull().ShouldContain("no steps");
        }

        [Fact]
        public void Random_needs_a_minimum_duration_not_above_the_maximum()
        {
            var options = new ChaosMonkeyOptions
            {
                Mode = ChaosMonkeyMode.Random,
                Random = new() { MinDuration = TimeSpan.FromMinutes(5), MaxDuration = TimeSpan.FromMinutes(1) }
            };

            Validator.Validate(null, options).FailureMessage.ShouldNotBeNull().ShouldContain("MinDuration");
        }
    }
}

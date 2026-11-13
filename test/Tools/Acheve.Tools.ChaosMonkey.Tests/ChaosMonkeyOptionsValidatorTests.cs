using Acheve.Common.Shared;
using Microsoft.Extensions.Configuration;

namespace Acheve.Tools.ChaosMonkey.Tests
{
    public class ChaosMonkeyOptionsValidatorTests
    {
        private static readonly ChaosMonkeyOptionsValidator Validator = new();

        // Every strategy configured with one valid empty step.
        private static ChaosMonkeyOptions AllStrategies() => new()
        {
            Strategies = Constants.ChaosMonkey.Strategies.ToDictionary(
                name => name,
                _ => new StrategyOptions { Steps = [new() { Duration = TimeSpan.FromMinutes(1) }] })
        };

        // Every strategy valid except the first one, which is this step.
        private static ChaosMonkeyOptions WithStep(ChaosStepOptions step)
        {
            var options = AllStrategies();
            options.Strategies[Constants.ChaosMonkey.Retries] = new StrategyOptions { Steps = [step] };
            return options;
        }

        private static ChaosMonkeyOptions WithChaos(string target, string scenario, TimeSpan? duration = null) =>
            WithStep(new() { Duration = duration ?? TimeSpan.FromMinutes(1), Chaos = { [target] = scenario } });

        private static ChaosMonkeyOptions WithResilience(string caller, string profile) =>
            WithStep(new() { Duration = TimeSpan.FromMinutes(1), Resilience = { [caller] = profile } });

        private static string Failure(ChaosMonkeyOptions options) => Validator.Validate(null, options).FailureMessage.ShouldNotBeNull();

        [Fact]
        public void The_shipped_settings_are_valid()
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "chaosmonkey.appsettings.json"))
                .Build();
            var options = configuration.GetSection(ChaosMonkeyOptions.Section).Get<ChaosMonkeyOptions>()!;

            var result = Validator.Validate(null, options);

            result.Succeeded.ShouldBeTrue(result.FailureMessage);
            options.Strategies.Keys.ShouldBe(Constants.ChaosMonkey.Strategies, ignoreOrder: true);
        }

        [Fact]
        public void Every_strategy_with_a_valid_step_is_valid()
        {
            Validator.Validate(null, AllStrategies()).Succeeded.ShouldBeTrue();
        }

        [Fact]
        public void An_unknown_target_fails()
        {
            Failure(WithChaos("database", Constants.Chaos.Down)).ShouldContain("unknown target 'database'");
        }

        [Fact]
        public void A_scenario_the_target_does_not_support_fails()
        {
            Failure(WithChaos(Constants.Resources.Api, Constants.Chaos.LostCallbacks)).ShouldContain("api doesn't support 'lost-callbacks'");
        }

        [Fact]
        public void A_step_without_duration_fails()
        {
            Failure(WithChaos(Constants.Resources.Api, Constants.Chaos.Down, TimeSpan.Zero)).ShouldContain("Duration must be positive");
        }

        [Fact]
        public void A_strategy_without_steps_or_random_fails()
        {
            var options = AllStrategies();
            options.Strategies[Constants.ChaosMonkey.Retries] = new StrategyOptions();

            Failure(options).ShouldContain("Strategy 'retries' needs either Steps or Random");
        }

        [Fact]
        public void A_strategy_with_steps_and_random_fails()
        {
            var options = AllStrategies();
            options.Strategies[Constants.ChaosMonkey.Retries].Random = new RandomChaosOptions();

            Failure(options).ShouldContain("either Steps or Random");
        }

        [Fact]
        public void An_unknown_resilience_target_fails()
        {
            Failure(WithResilience(Constants.Resources.Api, Constants.Resilience.Standard)).ShouldContain("unknown resilience target 'api'");
        }

        [Fact]
        public void An_unknown_resilience_profile_fails()
        {
            Failure(WithResilience(Constants.Resources.ImageDownloads, "turbo")).ShouldContain("imagedownloads doesn't support profile 'turbo'");
        }

        [Fact]
        public void A_strategy_missing_from_the_settings_fails()
        {
            var options = AllStrategies();
            options.Strategies.Remove(Constants.ChaosMonkey.Hedging);

            Failure(options).ShouldContain("Strategy 'hedging' is not configured");
        }

        [Fact]
        public void A_strategy_not_in_the_constants_fails()
        {
            var options = AllStrategies();
            options.Strategies["chaos-party"] = new StrategyOptions { Steps = [new() { Duration = TimeSpan.FromMinutes(1) }] };

            Failure(options).ShouldContain("Strategy 'chaos-party' has no dashboard command");
        }

        [Fact]
        public void Random_needs_a_minimum_duration_not_above_the_maximum()
        {
            var options = AllStrategies();
            options.Strategies[Constants.ChaosMonkey.Random] = new StrategyOptions
            {
                Random = new() { MinDuration = TimeSpan.FromMinutes(5), MaxDuration = TimeSpan.FromMinutes(1) }
            };

            Failure(options).ShouldContain("MinDuration");
        }
    }
}

using Acheve.Common.Shared;

namespace Acheve.Tools.ChaosMonkey.Tests
{
    public class ChaosPlanTests
    {
        private static ChaosMonkeyOptions Timeline(bool repeat) => new()
        {
            Mode = ChaosMonkeyMode.Timeline,
            Repeat = repeat,
            Timeline =
            [
                new() { Name = "Baseline", Duration = TimeSpan.FromMinutes(1) },
                new() { Duration = TimeSpan.FromMinutes(2), Scenarios = { [Constants.Resources.ExternalEstimations] = Constants.Chaos.Down } }
            ]
        };

        [Fact]
        public void A_timeline_without_repeat_runs_its_steps_once_in_order()
        {
            var steps = ChaosPlan.Timeline(Timeline(repeat: false)).ToList();

            steps.Select(s => s.Name).ShouldBe(["Baseline", "Step 2"]);
            steps[1].Duration.ShouldBe(TimeSpan.FromMinutes(2));
        }

        [Fact]
        public void A_repeating_timeline_starts_again_after_the_last_step()
        {
            var steps = ChaosPlan.Timeline(Timeline(repeat: true)).Take(5).Select(s => s.Name).ToList();

            steps.ShouldBe(["Baseline", "Step 2", "Baseline", "Step 2", "Baseline"]);
        }

        [Fact]
        public void Targets_a_step_does_not_name_are_healthy()
        {
            var step = ChaosPlan.Timeline(Timeline(repeat: false)).Last();

            step.ScenarioFor(Constants.Resources.ExternalEstimations).ShouldBe(Constants.Chaos.Down);
            step.ScenarioFor(Constants.Resources.Api).ShouldBe(Constants.Chaos.Healthy);
        }

        [Fact]
        public void Random_steps_use_supported_scenarios_within_the_limits_and_alternate_with_healthy_gaps()
        {
            var options = new RandomChaosOptions
            {
                MinDuration = TimeSpan.FromSeconds(30),
                MaxDuration = TimeSpan.FromSeconds(90),
                HealthyGap = TimeSpan.FromSeconds(20),
                MaxTargets = 2
            };

            var steps = ChaosPlan.RandomSteps(options, new Random(3)).Take(400).ToList();

            var chaos = steps.Where((_, i) => i % 2 == 0).ToList();
            var gaps = steps.Where((_, i) => i % 2 == 1).ToList();

            gaps.ShouldAllBe(s => s.Scenarios.Count == 0 && s.Duration == options.HealthyGap);
            chaos.ShouldAllBe(s => s.Scenarios.Count >= 1 && s.Scenarios.Count <= 2);
            chaos.ShouldAllBe(s => s.Duration >= options.MinDuration && s.Duration <= options.MaxDuration);
            chaos.SelectMany(s => s.Scenarios).ShouldAllBe(s => s.Value != Constants.Chaos.Healthy && ChaosTargets.Supports(s.Key, s.Value));

            // Over many steps every target and every scenario shows up.
            chaos.SelectMany(s => s.Scenarios.Keys).Distinct().ShouldBe(ChaosTargets.Names, ignoreOrder: true);
            chaos.SelectMany(s => s.Scenarios.Values).Distinct().Count().ShouldBe(Constants.Chaos.CallbackScenarios.Length - 1);
        }

        [Fact]
        public void Random_steps_follow_each_other_without_a_gap_when_it_is_zero()
        {
            var options = new RandomChaosOptions { HealthyGap = TimeSpan.Zero };

            ChaosPlan.RandomSteps(options, new Random(3)).Take(20).ShouldAllBe(s => s.Scenarios.Count > 0);
        }
    }
}

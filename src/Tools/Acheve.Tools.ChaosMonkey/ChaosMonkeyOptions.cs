using Acheve.Common.Shared;
using Microsoft.Extensions.Options;

namespace Acheve.Tools.ChaosMonkey
{
    public enum ChaosMonkeyMode
    {
        Timeline,
        Random
    }

    /// <summary>The ChaosMonkey configuration section.</summary>
    public sealed class ChaosMonkeyOptions
    {
        public const string Section = "ChaosMonkey";

        public ChaosMonkeyMode Mode { get; set; } = ChaosMonkeyMode.Timeline;

        public bool Repeat { get; set; } = true;

        public List<ChaosStepOptions> Timeline { get; set; } = [];

        public RandomChaosOptions Random { get; set; } = new();
    }

    public sealed class ChaosStepOptions
    {
        public string? Name { get; set; }

        public TimeSpan Duration { get; set; }

        /// <summary>Scenario per target; the targets not named go back to healthy.</summary>
        public Dictionary<string, string> Scenarios { get; set; } = [];
    }

    public sealed class RandomChaosOptions
    {
        public TimeSpan MinDuration { get; set; } = TimeSpan.FromMinutes(1);

        public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(3);

        public TimeSpan HealthyGap { get; set; } = TimeSpan.FromMinutes(1);

        public int MaxTargets { get; set; } = 1;
    }

    /// <summary>Fails at start on a step that names an unknown target or a scenario the target doesn't support.</summary>
    internal sealed class ChaosMonkeyOptionsValidator : IValidateOptions<ChaosMonkeyOptions>
    {
        public ValidateOptionsResult Validate(string? name, ChaosMonkeyOptions options)
        {
            var errors = new List<string>();

            if (options.Mode == ChaosMonkeyMode.Timeline)
            {
                if (options.Timeline.Count == 0)
                {
                    errors.Add("The timeline has no steps.");
                }

                for (var i = 0; i < options.Timeline.Count; i++)
                {
                    var step = options.Timeline[i];
                    var label = $"Timeline step {i + 1} ({step.Name ?? "unnamed"})";

                    if (step.Duration <= TimeSpan.Zero)
                    {
                        errors.Add($"{label}: Duration must be positive.");
                    }

                    foreach (var (target, scenario) in step.Scenarios)
                    {
                        if (!ChaosTargets.Scenarios.ContainsKey(target))
                        {
                            errors.Add($"{label}: unknown target '{target}'. Use one of: {string.Join(", ", ChaosTargets.Names)}.");
                        }
                        else if (!ChaosTargets.Supports(target, scenario))
                        {
                            errors.Add($"{label}: {target} doesn't support '{scenario}'. Use one of: {string.Join(", ", ChaosTargets.Scenarios[target])}.");
                        }
                    }
                }
            }
            else
            {
                var random = options.Random;

                if (random.MinDuration <= TimeSpan.Zero || random.MaxDuration < random.MinDuration)
                {
                    errors.Add("Random: MinDuration must be positive and not above MaxDuration.");
                }

                if (random.HealthyGap < TimeSpan.Zero)
                {
                    errors.Add("Random: HealthyGap can't be negative.");
                }

                if (random.MaxTargets < 1 || random.MaxTargets > ChaosTargets.Scenarios.Count)
                {
                    errors.Add($"Random: MaxTargets must be between 1 and {ChaosTargets.Scenarios.Count}.");
                }
            }

            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }
}

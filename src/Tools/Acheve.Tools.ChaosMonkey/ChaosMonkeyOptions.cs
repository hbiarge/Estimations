using Acheve.Common.Shared;
using Microsoft.Extensions.Options;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>The ChaosMonkey configuration section: the strategies it can run, by name.</summary>
    public sealed class ChaosMonkeyOptions
    {
        public const string Section = "ChaosMonkey";

        public Dictionary<string, StrategyOptions> Strategies { get; set; } = [];
    }

    /// <summary>A strategy: either steps in order (over and over when Repeat is on) or random chaos.</summary>
    public sealed class StrategyOptions
    {
        public bool Repeat { get; set; }

        public List<ChaosStepOptions> Steps { get; set; } = [];

        public RandomChaosOptions? Random { get; set; }
    }

    public sealed class ChaosStepOptions
    {
        public string? Name { get; set; }

        public TimeSpan Duration { get; set; }

        /// <summary>Chaos scenario per target; the targets not named go back to healthy.</summary>
        public Dictionary<string, string> Chaos { get; set; } = [];

        /// <summary>Resilience profile per caller service; the callers not named go back to standard.</summary>
        public Dictionary<string, string> Resilience { get; set; } = [];

        /// <summary>Consumption state (paused or running) per worker; the workers not named go back to running.</summary>
        public Dictionary<string, string> Consumption { get; set; } = [];
    }

    public sealed class RandomChaosOptions
    {
        public TimeSpan MinDuration { get; set; } = TimeSpan.FromMinutes(1);

        public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(3);

        public TimeSpan HealthyGap { get; set; } = TimeSpan.FromMinutes(1);

        public int MaxTargets { get; set; } = 1;
    }

    /// <summary>
    /// Fails at start when the strategies don't match the dashboard commands (Constants.ChaosMonkey.Strategies),
    /// or a step names an unknown target or a scenario, profile or consumption state the target doesn't support.
    /// </summary>
    internal sealed class ChaosMonkeyOptionsValidator : IValidateOptions<ChaosMonkeyOptions>
    {
        public ValidateOptionsResult Validate(string? name, ChaosMonkeyOptions options)
        {
            var errors = new List<string>();

            foreach (var strategy in Constants.ChaosMonkey.Strategies.Where(s => !options.Strategies.ContainsKey(s)))
            {
                errors.Add($"Strategy '{strategy}' is not configured.");
            }

            foreach (var (strategy, settings) in options.Strategies)
            {
                if (!Constants.ChaosMonkey.Strategies.Contains(strategy))
                {
                    errors.Add($"Strategy '{strategy}' has no dashboard command. Add it to Constants.ChaosMonkey.Strategies.");
                }

                if (settings.Steps.Count > 0 == settings.Random is not null)
                {
                    errors.Add($"Strategy '{strategy}' needs either Steps or Random, not both.");
                }
                else if (settings.Random is { } random)
                {
                    ValidateRandom($"Strategy '{strategy}'", random, errors);
                }
                else
                {
                    for (var i = 0; i < settings.Steps.Count; i++)
                    {
                        var step = settings.Steps[i];
                        ValidateStep($"Strategy '{strategy}' step {i + 1} ({step.Name ?? "unnamed"})", step, errors);
                    }
                }
            }

            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }

        private static void ValidateStep(string label, ChaosStepOptions step, List<string> errors)
        {
            if (step.Duration <= TimeSpan.Zero)
            {
                errors.Add($"{label}: Duration must be positive.");
            }

            foreach (var (target, scenario) in step.Chaos)
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

            foreach (var (caller, profile) in step.Resilience)
            {
                if (!ResilienceTargets.Names.Contains(caller))
                {
                    errors.Add($"{label}: unknown resilience target '{caller}'. Use one of: {string.Join(", ", ResilienceTargets.Names)}.");
                }
                else if (!ResilienceTargets.Supports(caller, profile))
                {
                    errors.Add($"{label}: {caller} doesn't support profile '{profile}'. Use one of: {string.Join(", ", Constants.Resilience.Profiles)}.");
                }
            }

            foreach (var (worker, state) in step.Consumption)
            {
                if (!ConsumptionTargets.Names.Contains(worker))
                {
                    errors.Add($"{label}: unknown consumption target '{worker}'. Use one of: {string.Join(", ", ConsumptionTargets.Names)}.");
                }
                else if (!ConsumptionTargets.Supports(worker, state))
                {
                    errors.Add($"{label}: {worker} doesn't support '{state}'. Use one of: {string.Join(", ", Constants.Consumption.States)}.");
                }
            }
        }

        private static void ValidateRandom(string label, RandomChaosOptions random, List<string> errors)
        {
            if (random.MinDuration <= TimeSpan.Zero || random.MaxDuration < random.MinDuration)
            {
                errors.Add($"{label}: MinDuration must be positive and not above MaxDuration.");
            }

            if (random.HealthyGap < TimeSpan.Zero)
            {
                errors.Add($"{label}: HealthyGap can't be negative.");
            }

            if (random.MaxTargets < 1 || random.MaxTargets > ChaosTargets.Scenarios.Count)
            {
                errors.Add($"{label}: MaxTargets must be between 1 and {ChaosTargets.Scenarios.Count}.");
            }
        }
    }
}

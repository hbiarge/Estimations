using Acheve.Common.Shared;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>
    /// One period of a strategy: these chaos scenarios and resilience profiles for this long;
    /// every other target is healthy and every other caller standard.
    /// </summary>
    internal sealed record ChaosStep(
        string Name,
        TimeSpan Duration,
        IReadOnlyDictionary<string, string> Chaos,
        IReadOnlyDictionary<string, string> Resilience)
    {
        private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

        public ChaosStep(string name, TimeSpan duration, IReadOnlyDictionary<string, string> chaos)
            : this(name, duration, chaos, None)
        {
        }

        public string ScenarioFor(string target) => Chaos.GetValueOrDefault(target, Constants.Chaos.Healthy);

        public string ProfileFor(string caller) => Resilience.GetValueOrDefault(caller, Constants.Resilience.Standard);

        public override string ToString()
        {
            var parts = new[] { Chaos, Resilience }
                .Where(settings => settings.Count > 0)
                .Select(settings => string.Join(", ", settings.Select(s => $"{s.Key} {s.Value}")))
                .ToList();

            return $"{Name}: {(parts.Count == 0 ? "everything healthy" : string.Join("; ", parts))} for {Duration}";
        }
    }

    internal static class ChaosPlan
    {
        public static IEnumerable<ChaosStep> Create(StrategyOptions strategy, Random random) =>
            strategy.Random is { } options ? RandomSteps(options, random) : Steps(strategy);

        /// <summary>The configured steps, in order; over and over when Repeat is on.</summary>
        public static IEnumerable<ChaosStep> Steps(StrategyOptions strategy)
        {
            var steps = strategy.Steps
                .Select((step, i) => new ChaosStep(step.Name ?? $"Step {i + 1}", step.Duration, step.Chaos, step.Resilience))
                .ToList();

            do
            {
                foreach (var step in steps)
                {
                    yield return step;
                }
            }
            while (strategy.Repeat && steps.Count > 0);
        }

        /// <summary>
        /// Endless random steps: 1 to MaxTargets targets with a scenario other than healthy, then a healthy gap.
        /// Resilience stays standard.
        /// </summary>
        public static IEnumerable<ChaosStep> RandomSteps(RandomChaosOptions options, Random random)
        {
            var targets = ChaosTargets.Names.ToArray();

            while (true)
            {
                var count = random.Next(1, options.MaxTargets + 1);
                var scenarios = new Dictionary<string, string>();

                foreach (var target in targets.OrderBy(_ => random.Next()).Take(count))
                {
                    var choices = ChaosTargets.Scenarios[target].Where(s => s != Constants.Chaos.Healthy).ToArray();
                    scenarios[target] = choices[random.Next(choices.Length)];
                }

                var duration = options.MinDuration + (options.MaxDuration - options.MinDuration) * random.NextDouble();
                yield return new ChaosStep("Random", RoundToSeconds(duration), scenarios);

                if (options.HealthyGap > TimeSpan.Zero)
                {
                    yield return new ChaosStep("Healthy gap", options.HealthyGap, new Dictionary<string, string>());
                }
            }
        }

        private static TimeSpan RoundToSeconds(TimeSpan value) => TimeSpan.FromSeconds(Math.Max(1, Math.Round(value.TotalSeconds)));
    }
}

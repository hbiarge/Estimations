using Acheve.Common.Shared;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>One period of chaos: these scenarios for this long; every other target is healthy.</summary>
    internal sealed record ChaosStep(string Name, TimeSpan Duration, IReadOnlyDictionary<string, string> Scenarios)
    {
        public string ScenarioFor(string target) => Scenarios.GetValueOrDefault(target, Constants.Chaos.Healthy);

        public override string ToString() => Scenarios.Count == 0
            ? $"{Name}: everything healthy for {Duration}"
            : $"{Name}: {string.Join(", ", Scenarios.Select(s => $"{s.Key} {s.Value}"))} for {Duration}";
    }

    internal static class ChaosPlan
    {
        public static IEnumerable<ChaosStep> Create(ChaosMonkeyOptions options, Random random) => options.Mode switch
        {
            ChaosMonkeyMode.Random => RandomSteps(options.Random, random),
            _ => Timeline(options)
        };

        /// <summary>The configured steps, in order; over and over when Repeat is on.</summary>
        public static IEnumerable<ChaosStep> Timeline(ChaosMonkeyOptions options)
        {
            var steps = options.Timeline
                .Select((step, i) => new ChaosStep(step.Name ?? $"Step {i + 1}", step.Duration, step.Scenarios))
                .ToList();

            do
            {
                foreach (var step in steps)
                {
                    yield return step;
                }
            }
            while (options.Repeat && steps.Count > 0);
        }

        /// <summary>Endless random steps: 1 to MaxTargets targets with a scenario other than healthy, then a healthy gap.</summary>
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

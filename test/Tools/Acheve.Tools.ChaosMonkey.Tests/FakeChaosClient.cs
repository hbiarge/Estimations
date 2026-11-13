using System.Collections.Concurrent;

namespace Acheve.Tools.ChaosMonkey.Tests
{
    /// <summary>Records every switch; the targets named as unreachable answer false.</summary>
    internal sealed class FakeChaosClient(params string[] unreachable) : IChaosClient
    {
        public const string Chaos = "chaos";
        public const string Resilience = "resilience";

        public ConcurrentQueue<(string Kind, string Target, string Value)> Calls { get; } = new();

        public Task<bool> SetScenarioAsync(string target, string scenario, CancellationToken cancellationToken) =>
            Record(Chaos, target, scenario);

        public Task<bool> SetProfileAsync(string caller, string profile, CancellationToken cancellationToken) =>
            Record(Resilience, caller, profile);

        private Task<bool> Record(string kind, string target, string value)
        {
            Calls.Enqueue((kind, target, value));
            return Task.FromResult(!unreachable.Contains(target));
        }
    }
}

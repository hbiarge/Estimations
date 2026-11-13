using System.Collections.Concurrent;

namespace Acheve.Tools.ChaosMonkey.Tests
{
    /// <summary>
    /// Records every switch; the targets named as unreachable answer false.
    /// Throw, when set, gets the call's number (from 0) and target and can return an exception to throw.
    /// </summary>
    internal sealed class FakeChaosClient(params string[] unreachable) : IChaosClient
    {
        private int _count;

        public Func<int, string, Exception?>? Throw { get; init; }

        public const string Chaos = "chaos";
        public const string Resilience = "resilience";
        public const string Consumption = "consumption";

        public ConcurrentQueue<(string Kind, string Target, string Value)> Calls { get; } = new();

        public Task<bool> SetScenarioAsync(string target, string scenario, CancellationToken cancellationToken) =>
            Record(Chaos, target, scenario);

        public Task<bool> SetProfileAsync(string caller, string profile, CancellationToken cancellationToken) =>
            Record(Resilience, caller, profile);

        public Task<bool> SetConsumptionAsync(string worker, string state, CancellationToken cancellationToken) =>
            Record(Consumption, worker, state);

        private Task<bool> Record(string kind, string target, string value)
        {
            Calls.Enqueue((kind, target, value));

            if (Throw?.Invoke(Interlocked.Increment(ref _count) - 1, target) is { } exception)
            {
                return Task.FromException<bool>(exception);
            }

            return Task.FromResult(!unreachable.Contains(target));
        }
    }
}

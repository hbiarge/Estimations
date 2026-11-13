using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Acheve.Common.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging.Abstractions;
using Polly.CircuitBreaker;

namespace Acheve.Common.Resilience.Tests
{
    public class ResiliencePipelinesTests
    {
        private const string ClientName = "external";

        private static readonly HttpRequestOptionsKey<string> PeerServiceKey = new("peer.service");

        private readonly ResilienceState _state;
        private readonly ResiliencePipelines _pipelines;

        public ResiliencePipelinesTests()
        {
            var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>();
            _state = new ResilienceState(ResilienceSettings.Profiles[Constants.Resilience.None], meterFactory, NullLogger<ResilienceState>.Instance);
            _pipelines = new ResiliencePipelines(_state, NullLoggerFactory.Instance, TimeProvider.System);
        }

        private HttpClient CreateClient(FakeExternalSystem external) =>
            new(new ResilienceHandler(_ => _pipelines.Get(ClientName)) { InnerHandler = external })
            {
                BaseAddress = new Uri("http://external")
            };

        [Fact]
        public async Task Without_resilience_the_first_failure_reaches_the_caller()
        {
            var external = new FakeExternalSystem(_ => HttpStatusCode.ServiceUnavailable);
            using var client = CreateClient(external);

            using var response = await client.GetAsync("/work", TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            external.Calls.ShouldBe(1);
        }

        [Fact]
        public async Task Retries_hide_a_transient_failure_once_switched_on()
        {
            var external = new FakeExternalSystem(call => call == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            using var client = CreateClient(external);

            _state.Set(new ResilienceSettings { Profile = "test", Retry = new RetrySettings { BaseDelay = TimeSpan.Zero } });
            using var response = await client.GetAsync("/work", TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            external.Calls.ShouldBe(2);
        }

        [Fact]
        public void Keeps_the_pipeline_while_the_settings_stay_the_same()
        {
            _state.TrySetProfile(Constants.Resilience.Standard);
            var first = _pipelines.Get(ClientName);

            // Setting the same profile again must not reset the circuit breaker.
            _state.TrySetProfile(Constants.Resilience.Standard);
            _pipelines.Get(ClientName).ShouldBeSameAs(first);

            _state.TrySetProfile(Constants.Resilience.FailFast);
            _pipelines.Get(ClientName).ShouldNotBeSameAs(first);
        }

        [Fact]
        public async Task An_open_circuit_stops_the_calls()
        {
            var external = new FakeExternalSystem(_ => HttpStatusCode.ServiceUnavailable);
            using var client = CreateClient(external);

            _state.Set(new ResilienceSettings
            {
                Profile = "test",
                CircuitBreaker = new CircuitBreakerSettings { FailureRatio = 0.5, MinimumThroughput = 2, BreakDuration = TimeSpan.FromMinutes(1) }
            });

            (await client.GetAsync("/work", TestContext.Current.CancellationToken)).Dispose();
            (await client.GetAsync("/work", TestContext.Current.CancellationToken)).Dispose();

            await Should.ThrowAsync<BrokenCircuitException>(() => client.GetAsync("/work", TestContext.Current.CancellationToken));
            external.Calls.ShouldBe(2);
        }

        [Fact]
        public async Task Hedging_answers_with_a_copy_when_the_first_attempt_is_slow()
        {
            // The first attempt hangs; the hedged one answers straight away.
            var external = new FakeExternalSystem(_ => HttpStatusCode.OK, hangOnFirstCall: true);
            using var client = CreateClient(external);

            _state.Set(new ResilienceSettings
            {
                Profile = "test",
                Hedging = new HedgingSettings { MaxHedgedAttempts = 1, Delay = TimeSpan.FromMilliseconds(50) }
            });

            using var request = new HttpRequestMessage(HttpMethod.Post, "/work")
            {
                Content = new StringContent("{\"case\":1}", Encoding.UTF8, "application/json")
            };
            request.Headers.Add(Idempotency.HeaderName, "case-1");
            request.Options.Set(PeerServiceKey, "external-estimations");

            using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            external.Calls.ShouldBe(2);

            var (first, hedged) = (external.Requests[0], external.Requests[1]);
            hedged.Request.ShouldNotBeSameAs(first.Request);
            hedged.Body.ShouldBe("{\"case\":1}");
            hedged.Request.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
            hedged.Request.Headers.GetValues(Idempotency.HeaderName).ShouldBe(["case-1"]);
            hedged.Request.Options.TryGetValue(PeerServiceKey, out var peerService).ShouldBeTrue();
            peerService.ShouldBe("external-estimations");
        }

        private sealed class FakeExternalSystem(Func<int, HttpStatusCode> answer, bool hangOnFirstCall = false) : HttpMessageHandler
        {
            private int _calls;

            public int Calls => _calls;

            public ConcurrentDictionary<int, (HttpRequestMessage Request, string? Body)> Requests { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var call = Interlocked.Increment(ref _calls);
                var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                Requests[call - 1] = (request, body);

                if (hangOnFirstCall && call == 1)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                return new HttpResponseMessage(answer(call));
            }
        }
    }
}

using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Acheve.Tools.ChaosMonkey.Tests
{
    public class ChaosClientTests
    {
        private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromException<HttpResponseMessage>(exception);
        }

        private sealed class Factory(Exception exception) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(new ThrowingHandler(exception));
        }

        public static TheoryData<Exception> Rejections => [new TimeoutRejectedException(), new BrokenCircuitException()];

        [Theory]
        [MemberData(nameof(Rejections))]
        public async Task A_call_the_resilience_handler_rejects_answers_false(Exception rejection)
        {
            var client = new ChaosClient(new Factory(rejection));

            (await client.SetScenarioAsync("api", "down", TestContext.Current.CancellationToken)).ShouldBeFalse();
            (await client.SetProfileAsync("imagedownloads", "none", TestContext.Current.CancellationToken)).ShouldBeFalse();
        }
    }
}

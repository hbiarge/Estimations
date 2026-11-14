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

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public List<Uri> Requests { get; } = [];

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add(request.RequestUri!);
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            }
        }

        private sealed class RecordingFactory(RecordingHandler handler) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
        }

        [Fact]
        public async Task Consumption_states_post_pause_and_resume()
        {
            var handler = new RecordingHandler();
            var client = new ChaosClient(new RecordingFactory(handler));

            (await client.SetConsumptionAsync("imagedownloads", "paused", TestContext.Current.CancellationToken)).ShouldBeTrue();
            (await client.SetConsumptionAsync("imagedownloads", "running", TestContext.Current.CancellationToken)).ShouldBeTrue();

            handler.Requests.Select(u => u.AbsolutePath).ShouldBe(["/consumption/pause", "/consumption/resume"]);
            handler.Requests.ShouldAllBe(u => u.Host == "imagedownloads");
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

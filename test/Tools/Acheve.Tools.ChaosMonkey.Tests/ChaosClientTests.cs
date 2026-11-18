using Microsoft.Extensions.Configuration;
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
            var client = new ChaosClient(new RecordingFactory(handler), new ConfigurationBuilder().Build());

            (await client.SetConsumptionAsync("imagedownloads", "paused", TestContext.Current.CancellationToken)).ShouldBeTrue();
            (await client.SetConsumptionAsync("imagedownloads", "running", TestContext.Current.CancellationToken)).ShouldBeTrue();

            handler.Requests.Select(u => u.AbsolutePath).ShouldBe(["/consumption/pause", "/consumption/resume"]);
            handler.Requests.ShouldAllBe(u => u.Host == "imagedownloads");
        }

        [Fact]
        public async Task The_notifications_target_switches_the_fake_of_every_customer()
        {
            var handler = new RecordingHandler();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Customers:Known:0"] = "acme", ["Customers:Known:1"] = "globex" })
                .Build();
            var client = new ChaosClient(new RecordingFactory(handler), configuration);

            (await client.SetScenarioAsync("external-notifications", "down", TestContext.Current.CancellationToken)).ShouldBeTrue();

            handler.Requests.Select(u => u.Host).ShouldBe(["external-notifications-acme", "external-notifications-globex"], ignoreOrder: true);
            handler.Requests.ShouldAllBe(u => u.AbsolutePath == "/chaos/scenarios/down");
        }

        public static TheoryData<Exception> Rejections => [new TimeoutRejectedException(), new BrokenCircuitException()];

        [Theory]
        [MemberData(nameof(Rejections))]
        public async Task A_call_the_resilience_handler_rejects_answers_false(Exception rejection)
        {
            var client = new ChaosClient(new Factory(rejection), new ConfigurationBuilder().Build());

            (await client.SetScenarioAsync("api", "down", TestContext.Current.CancellationToken)).ShouldBeFalse();
            (await client.SetProfileAsync("imagedownloads", "none", TestContext.Current.CancellationToken)).ShouldBeFalse();
        }
    }
}

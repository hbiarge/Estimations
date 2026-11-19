using System.Net;
using System.Net.Http.Json;
using System.Text;
using Acheve.Application.Api.Features.Estimations;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.TestHelpers.Events;

namespace Acheve.Application.Api.Tests
{
    // Who can see and start which case: the customer always comes from the token.
    public class EstimationsEndpointsTests
    {
        private const string Ticket = "11111111-1111-1111-1111-111111111111";

        private static StringContent NewEstimation(string? callback = null) =>
            new(
                $$"""{"callBackUri":"{{callback ?? "https://acme.example/notify"}}","imageUrls":["https://images.example/1.png","https://images.example/2.png"]}""",
                Encoding.UTF8,
                "application/json");

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Asks_for_a_token_on_both_endpoints()
        {
            await using var api = await EstimationsApi.StartAsync();

            using var read = await api.Client.SendAsync(api.Request(HttpMethod.Get, $"/estimations/{Ticket}"), Token);
            using var create = await api.Client.SendAsync(
                api.Request(HttpMethod.Post, "/estimations", content: NewEstimation()), Token);

            read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            create.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            api.Bus.Events.OfType<MessageSent<EstimationRequested>>().ShouldBeEmpty();
        }

        [Fact]
        public async Task Refuses_a_token_without_a_customer_and_starts_nothing()
        {
            await using var api = await EstimationsApi.StartAsync();

            using var read = await api.Client.SendAsync(
                api.Request(HttpMethod.Get, $"/estimations/{Ticket}", EstimationsApi.NoCustomer), Token);
            using var create = await api.Client.SendAsync(
                api.Request(HttpMethod.Post, "/estimations", EstimationsApi.NoCustomer, NewEstimation()), Token);

            read.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            api.StateHolder.Queries.ShouldBeEmpty();
            api.Bus.Events.OfType<MessageSent<EstimationRequested>>().ShouldBeEmpty();
        }

        [Fact]
        public async Task Shows_a_customer_their_own_case()
        {
            await using var api = await EstimationsApi.StartAsync();
            api.StateHolder.Add(Ticket, "acme", "CustomerNotified", result: "Failure", failureReason: "No images");

            var response = await api.Client.SendAsync(api.Request(HttpMethod.Get, $"/estimations/{Ticket}", "acme"), Token);
            var state = await response.Content.ReadFromJsonAsync<EstimationState>(Token);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            state.ShouldNotBeNull();
            state.Ticket.ShouldBe(Ticket);
            state.State.ShouldBe("CustomerNotified");
            state.Result.ShouldBe("Failure");
            state.FailureReason.ShouldBe("No images");
            state.Estimation.ShouldBeNull();
        }

        [Fact]
        public async Task Answers_404_when_another_customer_knows_the_ticket()
        {
            await using var api = await EstimationsApi.StartAsync();
            api.StateHolder.Add(Ticket, "acme", "WaitingForEstimation");

            using var response = await api.Client.SendAsync(
                api.Request(HttpMethod.Get, $"/estimations/{Ticket}", "globex"), Token);

            // The same answer as for a ticket nobody has: the customer can't tell the case exists.
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var query = api.StateHolder.Queries.ShouldHaveSingleItem();
            query.CustomerId.ShouldBe("globex");
            query.Ticket.ShouldBe(Ticket);
        }

        [Fact]
        public async Task Starts_the_case_for_the_customer_of_the_token()
        {
            await using var api = await EstimationsApi.StartAsync();

            using var response = await api.Client.SendAsync(
                api.Request(HttpMethod.Post, "/estimations", "acme", NewEstimation("https://acme.example/callback")), Token);
            var body = await response.Content.ReadFromJsonAsync<NewEstimationResponse>(Token);

            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            body.ShouldNotBeNull();
            response.Headers.Location!.ToString().ShouldBe($"/estimations/{body.Token}");

            var requested = api.Bus.Events.OfType<MessageSent<EstimationRequested>>().ShouldHaveSingleItem().CommandMessage;
            requested.CustomerId.ShouldBe("acme");
            requested.CaseNumber.ToString("D").ShouldBe(body.Token);
            requested.CallbackUrl.ShouldBe("https://acme.example/callback");
            requested.ImageUrls.ShouldBe(["https://images.example/1.png", "https://images.example/2.png"]);
        }

        [Fact]
        public async Task The_same_idempotency_key_of_a_customer_starts_one_case()
        {
            await using var api = await EstimationsApi.StartAsync();

            async Task<string> Post(string customer, string key)
            {
                var request = api.Request(HttpMethod.Post, "/estimations", customer, NewEstimation());
                request.Headers.Add(Idempotency.HeaderName, key);
                using var response = await api.Client.SendAsync(request, Token);
                response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

                return (await response.Content.ReadFromJsonAsync<NewEstimationResponse>(Token))!.Token;
            }

            var first = await Post("acme", "retry-1");

            // A repeated request is told apart by the case being known to the state holder.
            api.StateHolder.Add(first, "acme", "WaitingForImagesToBeDownloaded");
            var second = await Post("acme", "retry-1");

            second.ShouldBe(first);
            api.Bus.Events.OfType<MessageSent<EstimationRequested>>().Count().ShouldBe(1);
        }

        [Fact]
        public async Task The_same_idempotency_key_of_two_customers_is_two_cases()
        {
            await using var api = await EstimationsApi.StartAsync();

            async Task<string> Post(string customer)
            {
                var request = api.Request(HttpMethod.Post, "/estimations", customer, NewEstimation());
                request.Headers.Add(Idempotency.HeaderName, "shared-key");
                using var response = await api.Client.SendAsync(request, Token);

                return (await response.Content.ReadFromJsonAsync<NewEstimationResponse>(Token))!.Token;
            }

            var acme = await Post("acme");
            var globex = await Post("globex");

            // globex can't land on acme's case by choosing the same key.
            globex.ShouldNotBe(acme);
            api.Bus.Events.OfType<MessageSent<EstimationRequested>>().Count().ShouldBe(2);
        }

        [Fact]
        public async Task Refuses_an_idempotency_key_that_is_too_long()
        {
            await using var api = await EstimationsApi.StartAsync();
            var request = api.Request(HttpMethod.Post, "/estimations", "acme", NewEstimation());
            request.Headers.Add(Idempotency.HeaderName, new string('k', Idempotency.MaxKeyLength + 1));

            using var response = await api.Client.SendAsync(request, Token);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            api.Bus.Events.OfType<MessageSent<EstimationRequested>>().ShouldBeEmpty();
        }
    }
}

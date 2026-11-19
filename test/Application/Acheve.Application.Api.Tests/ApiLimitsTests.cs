using System.Net;
using System.Text;
using Acheve.Application.Api.Infrastructure;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;

namespace Acheve.Application.Api.Tests
{
    // Runs on a real Kestrel server on a free port: the body limit is Kestrel's.
    public class ApiLimitsTests
    {
        private sealed class TestApp(WebApplication app, HttpClient client) : IAsyncDisposable
        {
            public HttpClient Client => client;

            public async ValueTask DisposeAsync()
            {
                client.Dispose();
                await app.DisposeAsync();
            }
        }

        private static async Task<TestApp> StartAsync(Dictionary<string, string?> settings)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.AddApiLimits();

            var app = builder.Build();

            // The customer comes from a header here; the Api reads it from the token's claims.
            app.Use((context, next) =>
            {
                if (context.Request.Headers.TryGetValue("X-Customer", out var customer))
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(CustomerClaims.ClaimType, customer.ToString())], "test"));
                }

                return next(context);
            });

            app.UseApiLimits();

            app.MapPost("/estimations", ([FromBody] string body) => Results.Accepted())
                .RequireRateLimiting(ApiLimits.CreateEstimation);
            app.MapGet("/estimations/{ticket}", (string ticket) => Results.Ok())
                .RequireRateLimiting(ApiLimits.ReadEstimation);
            app.MapPost("/callbacks/{caseNumber}", ([FromBody] string body) => Results.Accepted())
                .RequireRateLimiting(ApiLimits.Callbacks);

            await app.StartAsync(TestContext.Current.CancellationToken);

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();

            return new TestApp(app, new HttpClient { BaseAddress = new Uri(address) });
        }

        private static StringContent Json(string text) =>
            new($"\"{text}\"", Encoding.UTF8, "application/json");

        private static HttpRequestMessage Request(HttpMethod method, string url, string? customer = null, HttpContent? content = null)
        {
            var request = new HttpRequestMessage(method, url) { Content = content };

            if (customer is not null)
            {
                request.Headers.Add("X-Customer", customer);
            }

            return request;
        }

        [Fact]
        public async Task Answers_413_to_a_body_over_the_limit()
        {
            await using var app = await StartAsync(new() { ["Limits:MaxRequestBodyBytes"] = "100" });

            using var small = await app.Client.SendAsync(
                Request(HttpMethod.Post, "/callbacks/1", content: Json(new string('a', 50))), TestContext.Current.CancellationToken);
            using var large = await app.Client.SendAsync(
                Request(HttpMethod.Post, "/callbacks/1", content: Json(new string('a', 500))), TestContext.Current.CancellationToken);

            small.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            large.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        }

        [Fact]
        public async Task Answers_429_with_retry_after_when_a_customer_asks_for_too_many_estimations()
        {
            await using var app = await StartAsync(new() { ["Limits:CreateEstimationsPerMinute"] = "2" });
            var token = TestContext.Current.CancellationToken;

            var statuses = new List<HttpStatusCode>();
            HttpResponseMessage? last = null;

            for (var i = 0; i < 3; i++)
            {
                last = await app.Client.SendAsync(Request(HttpMethod.Post, "/estimations", "acme", Json("x")), token);
                statuses.Add(last.StatusCode);
            }

            statuses.ShouldBe([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests]);
            last!.Headers.RetryAfter.ShouldNotBeNull();
            last.Headers.RetryAfter.Delta!.Value.TotalSeconds.ShouldBeInRange(1, 60);
        }

        [Fact]
        public async Task Limits_each_customer_on_their_own()
        {
            await using var app = await StartAsync(new() { ["Limits:CreateEstimationsPerMinute"] = "1" });
            var token = TestContext.Current.CancellationToken;

            using var acme = await app.Client.SendAsync(Request(HttpMethod.Post, "/estimations", "acme", Json("x")), token);
            using var acmeAgain = await app.Client.SendAsync(Request(HttpMethod.Post, "/estimations", "acme", Json("x")), token);
            using var globex = await app.Client.SendAsync(Request(HttpMethod.Post, "/estimations", "globex", Json("x")), token);

            acme.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            acmeAgain.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
            globex.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        [Fact]
        public async Task Counts_status_requests_apart_from_new_estimations()
        {
            await using var app = await StartAsync(new()
            {
                ["Limits:CreateEstimationsPerMinute"] = "1",
                ["Limits:ReadEstimationsPerMinute"] = "3"
            });
            var token = TestContext.Current.CancellationToken;

            using var created = await app.Client.SendAsync(Request(HttpMethod.Post, "/estimations", "acme", Json("x")), token);

            for (var i = 0; i < 3; i++)
            {
                using var read = await app.Client.SendAsync(Request(HttpMethod.Get, "/estimations/1", "acme"), token);
                read.StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            using var tooMany = await app.Client.SendAsync(Request(HttpMethod.Get, "/estimations/1", "acme"), token);

            created.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            tooMany.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        }

        [Fact]
        public async Task Limits_the_anonymous_callbacks_by_address()
        {
            await using var app = await StartAsync(new() { ["Limits:CallbacksPerMinute"] = "2" });
            var token = TestContext.Current.CancellationToken;

            var statuses = new List<HttpStatusCode>();

            for (var i = 0; i < 3; i++)
            {
                using var response = await app.Client.SendAsync(Request(HttpMethod.Post, "/callbacks/1", content: Json("x")), token);
                statuses.Add(response.StatusCode);
            }

            statuses.ShouldBe([HttpStatusCode.Accepted, HttpStatusCode.Accepted, HttpStatusCode.TooManyRequests]);
        }
    }
}

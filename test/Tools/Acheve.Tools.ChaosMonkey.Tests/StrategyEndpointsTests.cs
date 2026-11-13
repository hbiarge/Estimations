using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Acheve.Tools.ChaosMonkey.Tests
{
    public class StrategyEndpointsTests
    {
        private sealed record StrategiesResponse(string[] Strategies, StrategyStatus Running);

        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        private static async Task<(WebApplication App, HttpClient Client, FakeChaosClient Chaos)> StartAsync()
        {
            var chaos = new FakeChaosClient();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            builder.Services.AddMetrics();
            builder.Services.AddSingleton<IChaosClient>(chaos);
            builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
            builder.Services.AddSingleton(new Random(1));
            builder.Services.AddSingleton(services => new ChaosMonkeyMetrics(services.GetRequiredService<IMeterFactory>()));
            builder.Services.Configure<ChaosMonkeyOptions>(options => options.Strategies["test"] = new StrategyOptions
            {
                Steps = [new() { Duration = TimeSpan.FromMinutes(10), Chaos = { [Constants.Resources.Api] = Constants.Chaos.Down } }]
            });
            builder.Services.AddSingleton<StrategyRunner>();

            var app = builder.Build();
            app.MapStrategyEndpoints();
            await app.StartAsync(Cancellation);

            return (app, app.GetTestClient(), chaos);
        }

        [Fact]
        public async Task Get_lists_the_strategies_and_reports_idle()
        {
            var (app, client, _) = await StartAsync();
            await using var _app = app;

            var response = await client.GetFromJsonAsync<StrategiesResponse>("/strategies", Cancellation);

            response.ShouldNotBeNull();
            response.Strategies.ShouldBe(["test"]);
            response.Running.Strategy.ShouldBeNull();
        }

        [Fact]
        public async Task Post_a_strategy_starts_it()
        {
            var (app, client, _) = await StartAsync();
            await using var _app = app;

            var response = await client.PostAsync("/strategies/test", null, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await response.Content.ReadFromJsonAsync<StrategyStatus>(Cancellation)).ShouldNotBeNull().Strategy.ShouldBe("test");

            await app.Services.GetRequiredService<StrategyRunner>().CancelAsync(Cancellation);
        }

        [Fact]
        public async Task Post_an_unknown_strategy_is_404()
        {
            var (app, client, _) = await StartAsync();
            await using var _app = app;

            var response = await client.PostAsync("/strategies/nope", null, Cancellation);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadAsStringAsync(Cancellation)).ShouldContain("Unknown strategy 'nope'");
        }

        [Fact]
        public async Task Stop_and_next_when_idle_answer_200()
        {
            var (app, client, chaos) = await StartAsync();
            await using var _app = app;

            (await client.PostAsync("/strategies/stop", null, Cancellation)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await client.PostAsync("/strategies/next", null, Cancellation)).StatusCode.ShouldBe(HttpStatusCode.OK);

            chaos.Calls.ShouldBeEmpty();
        }
    }
}

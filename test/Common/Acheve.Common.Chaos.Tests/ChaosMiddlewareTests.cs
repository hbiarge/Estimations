using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Acheve.Common.Chaos.Tests
{
    public class ChaosMiddlewareTests
    {
        // A roll of 0 is under any rate above 0, so every enabled injection happens.
        private const double AlwaysInject = 0;

        private sealed class TestApp(WebApplication app) : IAsyncDisposable
        {
            // Created once the app has started.
            public HttpClient Client => field ??= app.GetTestClient();

            public ChaosState State => app.Services.GetRequiredService<ChaosState>();

            public IMeterFactory MeterFactory => app.Services.GetRequiredService<IMeterFactory>();

            public int EndpointCalls { get; set; }

            public async ValueTask DisposeAsync() => await app.DisposeAsync();
        }

        private static async Task<TestApp> StartAsync(
            string environment = "Development",
            string? scenario = null,
            string[]? paths = null)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();

            if (scenario is not null)
            {
                builder.Configuration["Chaos:Scenario"] = scenario;
            }

            for (var i = 0; i < (paths?.Length ?? 0); i++)
            {
                builder.Configuration[$"Chaos:Paths:{i}"] = paths![i];
            }

            builder.AddChaos();

            // Same state as AddChaos builds, with a roll that always injects.
            var registered = builder.Services.Last(s => s.ServiceType == typeof(ChaosState));
            builder.Services.AddSingleton(sp =>
            {
                var state = (ChaosState)registered.ImplementationFactory!(sp);
                return new ChaosState(
                    state.Current,
                    sp.GetRequiredService<IMeterFactory>(),
                    sp.GetRequiredService<ILogger<ChaosState>>(),
                    () => AlwaysInject);
            });

            var app = builder.Build();
            var testApp = new TestApp(app);

            app.UseChaos();
            app.MapGet("/work", () => { testApp.EndpointCalls++; return "done"; });
            app.MapGet("/callbacks/work", () => { testApp.EndpointCalls++; return "done"; });
            app.MapGet("/health", () => "healthy");

            await app.StartAsync(TestContext.Current.CancellationToken);

            return testApp;
        }

        [Fact]
        public async Task Healthy_lets_every_request_through()
        {
            await using var app = await StartAsync();

            var response = await app.Client.GetAsync("/work", TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            app.EndpointCalls.ShouldBe(1);
        }

        [Fact]
        public async Task A_fault_answers_without_running_the_endpoint()
        {
            await using var app = await StartAsync(scenario: Constants.Chaos.Down);
            using var injections = new MetricCollector<long>(app.MeterFactory, Constants.Telemetry.ChaosMeterName, "chaos.injections");

            var response = await app.Client.GetAsync("/work", TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            app.EndpointCalls.ShouldBe(0);
            injections.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["chaos.kind"].ShouldBe("fault");
        }

        [Fact]
        public async Task Health_checks_are_never_broken()
        {
            await using var app = await StartAsync(scenario: Constants.Chaos.Down);

            var response = await app.Client.GetAsync("/health", TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Only_the_configured_paths_get_the_chaos()
        {
            await using var app = await StartAsync(scenario: Constants.Chaos.Down, paths: ["/callbacks"]);

            var callback = await app.Client.GetAsync("/callbacks/work", TestContext.Current.CancellationToken);
            var other = await app.Client.GetAsync("/work", TestContext.Current.CancellationToken);

            callback.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            other.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        [Fact]
        public async Task Latency_delays_the_request_and_then_runs_it()
        {
            await using var app = await StartAsync();
            app.State.Set(new ChaosSettings { Scenario = "test", LatencyRate = 1, Latency = TimeSpan.FromMilliseconds(50) });
            using var injections = new MetricCollector<long>(app.MeterFactory, Constants.Telemetry.ChaosMeterName, "chaos.injections");

            var response = await app.Client.GetAsync("/work", TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            app.EndpointCalls.ShouldBe(1);
            injections.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["chaos.kind"].ShouldBe("latency");
        }

        [Fact]
        public async Task A_scenario_switched_through_the_endpoint_applies_straight_away()
        {
            await using var app = await StartAsync();

            var switched = await app.Client.PostAsync($"/chaos/scenarios/{Constants.Chaos.Down}", null, TestContext.Current.CancellationToken);
            var response = await app.Client.GetAsync("/work", TestContext.Current.CancellationToken);

            switched.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        [Fact]
        public async Task An_unknown_scenario_is_not_found()
        {
            await using var app = await StartAsync();

            var response = await app.Client.PostAsync("/chaos/scenarios/meteor-strike", null, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            app.State.Current.ShouldBe(ChaosSettings.Healthy);
        }

        [Fact]
        public async Task Custom_settings_need_rates_between_0_and_1()
        {
            await using var app = await StartAsync();

            var invalid = await app.Client.PutAsJsonAsync("/chaos", new ChaosSettings { FaultRate = 2 }, TestContext.Current.CancellationToken);
            var valid = await app.Client.PutAsJsonAsync("/chaos", new ChaosSettings { FaultRate = 0.5, FaultStatusCode = 500 }, TestContext.Current.CancellationToken);

            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            valid.StatusCode.ShouldBe(HttpStatusCode.OK);
            app.State.Current.ShouldBe(new ChaosSettings { Scenario = "custom", FaultRate = 0.5, FaultStatusCode = 500 });
        }

        [Fact]
        public async Task Outside_development_there_is_no_chaos_and_no_way_to_switch_it_on()
        {
            await using var app = await StartAsync(environment: "Production", scenario: Constants.Chaos.Down);

            var work = await app.Client.GetAsync("/work", TestContext.Current.CancellationToken);
            var chaos = await app.Client.PostAsync($"/chaos/scenarios/{Constants.Chaos.Down}", null, TestContext.Current.CancellationToken);

            work.StatusCode.ShouldBe(HttpStatusCode.OK);
            chaos.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }
}

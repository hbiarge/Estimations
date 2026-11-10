using Acheve.Common.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Acheve.Common.Chaos
{
    public static class ChaosExtensions
    {
        /// <summary>
        /// Registers the chaos state. It starts with the scenario in Chaos:Scenario (healthy by default).
        /// Outside Development the chaos stays healthy and can't be changed.
        /// </summary>
        public static TBuilder AddChaos<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.Services.Configure<ChaosOptions>(builder.Configuration.GetSection(Constants.Chaos.ConfigurationSection));
            builder.Services.AddMetrics();
            builder.Services.TryAddTimeProvider();

            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<ChaosOptions>>().Value;
                var scenario = builder.Environment.IsDevelopment() ? options.Scenario ?? Constants.Chaos.Healthy : Constants.Chaos.Healthy;

                if (!ChaosSettings.Scenarios.TryGetValue(scenario, out var initial))
                {
                    throw new InvalidOperationException(
                        $"Unknown chaos scenario '{scenario}'. Use one of: {string.Join(", ", ChaosSettings.Scenarios.Keys)}.");
                }

                return ActivatorUtilities.CreateInstance<ChaosState>(sp, initial);
            });

            return builder;
        }

        /// <summary>
        /// In Development, injects the chaos into incoming requests and maps the /chaos endpoints:
        /// GET /chaos (current settings), PUT /chaos (custom settings) and POST /chaos/scenarios/{name}.
        /// </summary>
        public static WebApplication UseChaos(this WebApplication app)
        {
            // Creates the state now, so its metrics report the scenario from the start.
            app.Services.GetRequiredService<ChaosState>();

            if (!app.Environment.IsDevelopment())
            {
                return app;
            }

            app.UseMiddleware<ChaosMiddleware>();
            app.MapChaosEndpoints();

            return app;
        }

        internal static IEndpointRouteBuilder MapChaosEndpoints(this IEndpointRouteBuilder app)
        {
            var chaos = app.MapGroup("/chaos").ExcludeFromDescription();

            chaos.MapGet("/", (ChaosState state) => TypedResults.Ok(new ChaosStatus(state.Current, ChaosSettings.Scenarios.Keys)));

            chaos.MapPut("/", (ChaosSettings settings, ChaosState state) =>
            {
                if (!IsRate(settings.FaultRate) || !IsRate(settings.LatencyRate) ||
                    !IsRate(settings.CallbackDropRate) || !IsRate(settings.CallbackDuplicateRate))
                {
                    return Results.BadRequest("Rates go from 0 to 1.");
                }

                state.Set(settings with { Scenario = ChaosState.CustomScenario });
                return Results.Ok(state.Current);
            });

            chaos.MapPost("/scenarios/{name}", (string name, ChaosState state) =>
                state.TrySetScenario(name)
                    ? Results.Ok(state.Current)
                    : Results.NotFound($"Unknown chaos scenario '{name}'. Use one of: {string.Join(", ", ChaosSettings.Scenarios.Keys)}."));

            return app;
        }

        private static bool IsRate(double value) => value is >= 0 and <= 1;

        private static void TryAddTimeProvider(this IServiceCollection services)
        {
            if (services.All(s => s.ServiceType != typeof(TimeProvider)))
            {
                services.AddSingleton(TimeProvider.System);
            }
        }
    }

    public sealed record ChaosStatus(ChaosSettings Current, IEnumerable<string> Scenarios);
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using System.Diagnostics.Metrics;

namespace Acheve.Common.Consumption
{
    public static class ConsumptionExtensions
    {
        /// <summary>Registers the consumption state of the service's Rebus worker. It starts running.</summary>
        public static TBuilder AddConsumption<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.Services.AddMetrics();
            builder.Services.AddSingleton(sp => new ConsumptionState(
                () => sp.GetRequiredService<IBus>().Advanced.Workers,
                sp.GetRequiredService<IMeterFactory>(),
                sp.GetRequiredService<ILogger<ConsumptionState>>()));

            return builder;
        }

        /// <summary>
        /// In Development, maps the /consumption endpoints: GET /consumption (current state),
        /// POST /consumption/pause and POST /consumption/resume.
        /// </summary>
        public static WebApplication UseConsumption(this WebApplication app)
        {
            // Creates the state now, so its metric reports from the start.
            app.Services.GetRequiredService<ConsumptionState>();

            if (app.Environment.IsDevelopment())
            {
                app.MapConsumptionEndpoints();
            }

            return app;
        }

        internal static IEndpointRouteBuilder MapConsumptionEndpoints(this IEndpointRouteBuilder app)
        {
            var consumption = app.MapGroup("/consumption").ExcludeFromDescription();

            consumption.MapGet("/", (ConsumptionState state) => TypedResults.Ok(new ConsumptionStatus(state.State)));

            consumption.MapPost("/pause", async (ConsumptionState state) =>
            {
                await state.PauseAsync();
                return TypedResults.Ok(new ConsumptionStatus(state.State));
            });

            consumption.MapPost("/resume", async (ConsumptionState state) =>
            {
                await state.ResumeAsync();
                return TypedResults.Ok(new ConsumptionStatus(state.State));
            });

            return app;
        }
    }

    public sealed record ConsumptionStatus(string State);
}

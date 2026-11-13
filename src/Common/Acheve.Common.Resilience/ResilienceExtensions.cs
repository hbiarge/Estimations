using Acheve.Common.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;

namespace Acheve.Common.Resilience
{
    public static class ResilienceExtensions
    {
        /// <summary>
        /// Registers the resilience state. It starts with the profile in Resilience:Profile (standard by default).
        /// </summary>
        public static TBuilder AddResilience<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.Services.AddMetrics();
            builder.Services.TryAddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<ResiliencePipelines>();

            var profile = builder.Configuration[$"{Constants.Resilience.ConfigurationSection}:Profile"] ?? Constants.Resilience.Standard;

            if (!ResilienceSettings.Profiles.TryGetValue(profile, out var initial))
            {
                throw new InvalidOperationException(
                    $"Unknown resilience profile '{profile}'. Use one of: {string.Join(", ", ResilienceSettings.Profiles.Keys)}.");
            }

            builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<ResilienceState>(sp, initial));

            return builder;
        }

        /// <summary>
        /// Sends the client's requests through the resilience settings in force (see <see cref="ResilienceState"/>),
        /// instead of the fixed pipeline of AddStandardResilienceHandler().
        /// </summary>
        public static IHttpClientBuilder AddSwitchableResilienceHandler(this IHttpClientBuilder builder)
        {
            var clientName = builder.Name;

            builder.AddHttpMessageHandler(sp =>
            {
                var pipelines = sp.GetRequiredService<ResiliencePipelines>();
                return new ResilienceHandler(_ => pipelines.Get(clientName));
            });

            return builder;
        }

        /// <summary>
        /// In Development, maps the /resilience endpoints: GET /resilience (current settings),
        /// PUT /resilience (custom settings) and POST /resilience/profiles/{name}.
        /// </summary>
        public static WebApplication UseResilience(this WebApplication app)
        {
            // Creates the state now, so its metrics report the profile from the start.
            app.Services.GetRequiredService<ResilienceState>();

            if (app.Environment.IsDevelopment())
            {
                app.MapResilienceEndpoints();
            }

            return app;
        }

        internal static IEndpointRouteBuilder MapResilienceEndpoints(this IEndpointRouteBuilder app)
        {
            var resilience = app.MapGroup("/resilience").ExcludeFromDescription();

            resilience.MapGet("/", (ResilienceState state) =>
                TypedResults.Ok(new ResilienceStatus(state.Current, ResilienceSettings.Profiles.Keys)));

            resilience.MapPut("/", (ResilienceSettings settings, ResilienceState state) =>
            {
                if (ResiliencePipelines.Validate(settings) is { } error)
                {
                    return Results.BadRequest(error);
                }

                state.Set(settings with { Profile = ResilienceState.CustomProfile });
                return Results.Ok(state.Current);
            });

            resilience.MapPost("/profiles/{name}", (string name, ResilienceState state) =>
                state.TrySetProfile(name)
                    ? Results.Ok(state.Current)
                    : Results.NotFound($"Unknown resilience profile '{name}'. Use one of: {string.Join(", ", ResilienceSettings.Profiles.Keys)}."));

            return app;
        }
    }

    public sealed record ResilienceStatus(ResilienceSettings Current, IEnumerable<string> Profiles);
}

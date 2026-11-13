using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Rebus.OpenTelemetry.Configuration;

namespace Microsoft.Extensions.Hosting
{
    // Shared setup for every service: OpenTelemetry, health checks and service discovery.
    // See https://aka.ms/dotnet/aspire/service-defaults
    public static class Extensions
    {
        private const string HealthEndpointPath = "/health";
        private const string AlivenessEndpointPath = "/alive";

        // The AppHost sets it to tell the application's telemetry apart from the external systems' telemetry.
        private const string ServiceNamespaceKey = "Telemetry:ServiceNamespace";

        public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.ConfigureOpenTelemetry();

            builder.AddDefaultHealthChecks();

            builder.Services.AddServiceDiscovery();

            // Unlike the Aspire template, this doesn't add a resilience handler to every HttpClient:
            // HTTP clients add their own (AddSwitchableResilienceHandler() or AddStandardResilienceHandler()),
            // and the gRPC client uses gRPC retries.
            builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());

            return builder;
        }

        public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

            var otel = builder.Services.AddOpenTelemetry();

            if (builder.Configuration[ServiceNamespaceKey] is { Length: > 0 } serviceNamespace)
            {
                otel.ConfigureResource(resource => resource.AddAttributes([new("service.namespace", serviceNamespace)]));
            }

            otel
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddRebusInstrumentation()
                    // Case-level metrics (Constants.Telemetry.MeterName in Acheve.Common.Shared)
                    .AddMeter("Acheve.Estimations")
                    // Chaos injections (Constants.Telemetry.ChaosMeterName)
                    .AddMeter("Acheve.Chaos")
                    // Traffic generator (Constants.Telemetry.TrafficMeterName)
                    .AddMeter("Acheve.Traffic")
                    // Chaos monkey (Constants.Telemetry.ChaosMonkeyMeterName)
                    .AddMeter("Acheve.ChaosMonkey")
                    // Resilience profile in force (Constants.Telemetry.ResilienceMeterName)
                    .AddMeter("Acheve.Resilience")
                    // Polly's events: retries, hedges, circuit breaker changes, timeouts (Constants.Telemetry.PollyMeterName)
                    .AddMeter("Polly"))
                .WithTracing(tracing => tracing
                    .AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(options =>
                        // Don't trace health check requests
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath) &&
                            !context.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                    .AddHttpClientInstrumentation(options =>
                        options.EnrichWithHttpRequestMessage = PeerService.Enrich)
                    .AddRebusInstrumentation());

            builder.AddOpenTelemetryExporters();

            return builder;
        }

        private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            // Aspire sets this variable to point at its dashboard; any other OTLP collector works too.
            var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

            if (useOtlpExporter)
            {
                builder.Services.AddOpenTelemetry().UseOtlpExporter();
            }

            return builder;
        }

        public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.Services.AddHealthChecks()
                // Default liveness check to ensure the app is responsive
                .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

            return builder;
        }

        public static WebApplication MapDefaultEndpoints(this WebApplication app)
        {
            // Health endpoints are only exposed in development.
            // See https://aka.ms/dotnet/aspire/healthchecks before enabling them elsewhere.
            if (app.Environment.IsDevelopment())
            {
                // All health checks must pass for the app to be considered ready to accept traffic
                app.MapHealthChecks(HealthEndpointPath);

                // Only health checks tagged with "live" must pass for the app to be considered alive
                app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
                {
                    Predicate = r => r.Tags.Contains("live")
                });
            }

            return app;
        }
    }
}

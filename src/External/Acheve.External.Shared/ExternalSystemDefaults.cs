using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Acheve.External.Shared
{
    public static class ExternalSystemDefaults
    {
        /// <summary>
        /// Service defaults for the fake external systems. They behave like systems owned by someone else:
        /// they don't continue the caller's trace and don't send their own trace back in the callbacks,
        /// so their spans never show up inside the application's traces.
        /// The application follows a case across the callbacks with the trace context it puts in the callback URL.
        /// </summary>
        public static TBuilder AddExternalSystemDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
        {
            builder.AddServiceDefaults();

            // HttpClient reads the static propagator for outgoing requests; ASP.NET Core takes it from DI for incoming ones.
            var propagator = new NoTraceContextPropagator();
            DistributedContextPropagator.Current = propagator;
            builder.Services.AddSingleton<DistributedContextPropagator>(propagator);

            // The OpenTelemetry ASP.NET Core instrumentation reads the incoming trace headers again with its own propagator.
            Sdk.SetDefaultTextMapPropagator(new NoTextMapPropagator());

            return builder;
        }

        /// <summary>OpenTelemetry's version of <see cref="NoTraceContextPropagator"/>.</summary>
        private sealed class NoTextMapPropagator : TextMapPropagator
        {
            public override ISet<string> Fields { get; } = new HashSet<string>();

            public override PropagationContext Extract<T>(PropagationContext context, T carrier, Func<T, string, IEnumerable<string>?> getter) =>
                context;

            public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
            {
            }
        }

        /// <summary>Ignores the trace headers of incoming requests and adds none to outgoing requests.</summary>
        private sealed class NoTraceContextPropagator : DistributedContextPropagator
        {
            public override IReadOnlyCollection<string> Fields { get; } = [];

            public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
            {
            }

            public override void ExtractTraceIdAndState(
                object? carrier,
                PropagatorGetterCallback? getter,
                out string? traceId,
                out string? traceState)
            {
                traceId = null;
                traceState = null;
            }

            public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(object? carrier, PropagatorGetterCallback? getter) =>
                null;
        }
    }
}

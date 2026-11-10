using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Simmy;
using Polly.Simmy.Fault;
using Polly.Simmy.Latency;

namespace Acheve.Common.Chaos
{
    /// <summary>
    /// Injects latency and faults into incoming requests with Polly's chaos strategies (Simmy),
    /// as the current <see cref="ChaosSettings"/> say. A fault answers with its status code without running the endpoint.
    /// </summary>
    internal sealed class ChaosMiddleware
    {
        // Never broken by the chaos: Aspire needs the health checks, and the chaos must stay controllable.
        private static readonly PathString[] AlwaysExcluded = ["/health", "/alive", "/chaos"];

        private readonly RequestDelegate _next;
        private readonly ChaosState _state;
        private readonly PathString[] _paths;
        private readonly ResiliencePipeline _pipeline;

        public ChaosMiddleware(RequestDelegate next, ChaosState state, IOptions<ChaosOptions> options, TimeProvider timeProvider)
        {
            _next = next;
            _state = state;
            _paths = options.Value.Paths.Select(p => new PathString(p)).ToArray();

            // The generators read the settings on every request, so a scenario change applies straight away.
            _pipeline = new ResiliencePipelineBuilder { TimeProvider = timeProvider }
                .AddChaosLatency(new ChaosLatencyStrategyOptions
                {
                    EnabledGenerator = _ => ValueTask.FromResult(_state.Current.LatencyRate > 0),
                    InjectionRateGenerator = _ => ValueTask.FromResult(_state.Current.LatencyRate),
                    LatencyGenerator = _ => ValueTask.FromResult(_state.Current.Latency),
                    Randomizer = _state.Random,
                    OnLatencyInjected = _ =>
                    {
                        _state.RecordInjection("latency");
                        return default;
                    }
                })
                .AddChaosFault(new ChaosFaultStrategyOptions
                {
                    EnabledGenerator = _ => ValueTask.FromResult(_state.Current.FaultRate > 0),
                    InjectionRateGenerator = _ => ValueTask.FromResult(_state.Current.FaultRate),
                    FaultGenerator = new FaultGenerator().AddException(() => new ChaosFaultException()),
                    Randomizer = _state.Random,
                    OnFaultInjected = _ =>
                    {
                        _state.RecordInjection("fault");
                        return default;
                    }
                })
                .Build();
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!AppliesTo(context.Request.Path))
            {
                await _next(context);
                return;
            }

            try
            {
                await _pipeline.ExecuteAsync(
                    static (state, _) => new ValueTask(state.next(state.context)),
                    (next: _next, context),
                    context.RequestAborted);
            }
            catch (ChaosFaultException) when (!context.Response.HasStarted)
            {
                var settings = _state.Current;

                context.Response.StatusCode = settings.FaultStatusCode;
                await context.Response.WriteAsync($"Fault injected by the chaos scenario '{settings.Scenario}'.", context.RequestAborted);
            }
        }

        private bool AppliesTo(PathString path)
        {
            if (AlwaysExcluded.Any(excluded => path.StartsWithSegments(excluded)))
            {
                return false;
            }

            return _paths.Length == 0 || _paths.Any(included => path.StartsWithSegments(included));
        }
    }

    internal sealed class ChaosFaultException() : Exception("Fault injected by the chaos.");
}

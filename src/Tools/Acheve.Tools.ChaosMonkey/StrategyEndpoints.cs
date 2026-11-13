using Microsoft.Extensions.Options;

namespace Acheve.Tools.ChaosMonkey
{
    internal sealed record StrategiesStatus(IEnumerable<string> Strategies, StrategyStatus Running);

    internal static class StrategyEndpoints
    {
        /// <summary>
        /// GET /strategies (the strategies and the one running), POST /strategies/{name} (start it, replacing the one
        /// running), POST /strategies/stop and POST /strategies/next (skip to the next step).
        /// </summary>
        public static IEndpointRouteBuilder MapStrategyEndpoints(this IEndpointRouteBuilder app)
        {
            var strategies = app.MapGroup("/strategies").ExcludeFromDescription();

            strategies.MapGet("/", (StrategyRunner runner, IOptions<ChaosMonkeyOptions> options) =>
                TypedResults.Ok(new StrategiesStatus(options.Value.Strategies.Keys, runner.Status)));

            strategies.MapPost("/stop", async (StrategyRunner runner, CancellationToken cancellationToken) =>
            {
                await runner.CancelAsync(cancellationToken);
                return TypedResults.Ok(runner.Status);
            });

            strategies.MapPost("/next", (StrategyRunner runner) =>
            {
                runner.SkipStep();
                return TypedResults.Ok(runner.Status);
            });

            strategies.MapPost("/{name}", async (string name, StrategyRunner runner, IOptions<ChaosMonkeyOptions> options, CancellationToken cancellationToken) =>
                await runner.RunAsync(name, cancellationToken)
                    ? Results.Ok(runner.Status)
                    : Results.NotFound($"Unknown strategy '{name}'. Use one of: {string.Join(", ", options.Value.Strategies.Keys)}."));

            return app;
        }
    }
}

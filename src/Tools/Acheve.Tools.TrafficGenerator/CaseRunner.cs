using Microsoft.Extensions.Options;

namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>Starts one case and polls its state until it ends or times out.</summary>
    internal sealed partial class CaseRunner(
        IEstimationsClient client,
        TrafficMetrics metrics,
        IOptions<TrafficOptions> options,
        TimeProvider time,
        ILogger<CaseRunner> logger)
    {
        public async Task<CaseOutcome> RunAsync(NewEstimationRequest request, CancellationToken cancellationToken)
        {
            var settings = options.Value;
            metrics.Started();

            var ticket = await client.StartAsync(request, cancellationToken);

            if (ticket is null)
            {
                metrics.Finished(CaseOutcome.Rejected, duration: null);
                LogRejected(request.ImageUrls.Length);
                return CaseOutcome.Rejected;
            }

            var started = time.GetTimestamp();
            var seen = new List<string>();
            CaseStatus? last = null;

            while (time.GetElapsedTime(started) < settings.CaseTimeout)
            {
                await Task.Delay(settings.PollInterval, time, cancellationToken);

                // A failed poll (null) just waits for the next one.
                if (await client.GetStateAsync(ticket, cancellationToken) is { } current)
                {
                    last = current;

                    if (!seen.Contains(current.State))
                    {
                        seen.Add(current.State);
                    }

                    if (CaseStates.IsFinal(current.State))
                    {
                        break;
                    }
                }
            }

            var outcome = CaseStates.Classify(last);
            var elapsed = time.GetElapsedTime(started);

            metrics.Finished(outcome, elapsed);
            LogFinished(ticket, outcome, elapsed.TotalSeconds, request.ImageUrls.Length, string.Join(" > ", seen));

            return outcome;
        }

        [LoggerMessage(LogLevel.Warning, "The Api didn't accept a case with {imageCount} images.")]
        private partial void LogRejected(int imageCount);

        [LoggerMessage(LogLevel.Information, "Case {ticket} ended {outcome} after {seconds:F1} s ({imageCount} images; states seen: {states}).")]
        private partial void LogFinished(string ticket, CaseOutcome outcome, double seconds, int imageCount, string states);
    }
}

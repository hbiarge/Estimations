namespace Acheve.Common.Messages
{
    // The states only move forward: the state holder ignores a state lower than the current one.
    public enum EstimationStates
    {
        // Stage 1: Download images
        WaitingForImagesToBeDownloaded = 10,

        // Stage 2: Analyse images
        WaitingForImagesToBeAnalysed = 20,

        // Stage 3: Estimate
        WaitingForEstimation = 30,
        EstimationReady = 31,
        EstimationError = 32,

        // Stage 4: Notify customer
        CustomerNotified = 40,
        CustomerNotificationError = 41
    }

    public static class EstimationStateTelemetry
    {
        public const string StateTag = "estimation.state";

        // Sorts before estimation.state as a Prometheus label (estimation_order < estimation_state), so the
        // series of a "by state" query come back in pipeline order instead of alphabetical order.
        public const string OrderTag = "estimation.order";

        public static string OrderValue(this EstimationStates state) => ((int)state).ToString("D2");
    }
}

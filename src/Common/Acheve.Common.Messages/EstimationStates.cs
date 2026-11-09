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

        // Stage 4: Notify client
        ClientNotified = 40,
        ClientNotificationError = 41
    }
}
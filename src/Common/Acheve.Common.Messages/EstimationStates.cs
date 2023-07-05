namespace Acheve.Common.Messages
{
    public enum EstimationStates
    {
        // Stage 1: Download images
        WaitingForImagesToBeDownloaded = 10,
        ImagesDownloaded,

        // Stage 2: Analyse images
        WaitingForImagesToBeAnalysed = 20,
        ImagesAnalysed,
        StuckWaitingForImagesToBeAnalysed,

        // Stage 3: Estimate
        WaitingForEstimation = 30,
        EstimationReady,
        EstimationError,

        // Stage 4: Notify client
        ClientNotified = 40,
        ClientNotificationError
    }
}
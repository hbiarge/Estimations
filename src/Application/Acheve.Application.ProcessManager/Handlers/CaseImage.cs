namespace Acheve.Application.ProcessManager.Handlers
{
    public class CaseImage
    {
        public const int MaxAnalysisWaits = 5;
        public static readonly TimeSpan AnalysisWaitTime = TimeSpan.FromSeconds(20);

        // The download worker's own limits are shorter; this only fires when its answer never arrives.
        public static readonly TimeSpan DownloadWaitTime = TimeSpan.FromMinutes(2);

        public const string WaitTimeExceeded = "Wait time exceeded";

        public int Id { get; set; }

        public required string Url { get; init; }

        public required string Extension { get; init; }

        public string? ImageTicket { get; set; }

        public string? DownloadError { get; set; }

        // An image is considered downloaded if
        // we have a ticket for the image (where the image is stored)
        // or if we couldn't download the image because an error
        public bool Downloaded => string.IsNullOrEmpty(ImageTicket) == false
                                  || string.IsNullOrEmpty(DownloadError) == false;

        public string? AnalysisTicket { get; set; }

        public string? AnalysisError { get; set; }

        public int CurrentAnalysisWaits { get; set; }

        // True once the external service accepted the image. The wait starts again once, at that moment.
        public bool AnalysisAccepted { get; set; }

        // An image is considered analysed if
        // we couldn't download the image because an error (so we didn't send the image to process)
        // we have a ticket for the analysis (where the metadata is stored)
        // or if we couldn't analyse the image because an error
        public bool Analyzed => string.IsNullOrEmpty(DownloadError) == false
                                 || string.IsNullOrEmpty(AnalysisTicket) == false
                                 || string.IsNullOrEmpty(AnalysisError) == false;

        public bool AvailableToEstimate => string.IsNullOrEmpty(AnalysisTicket) == false;

        // Why the image is left out of the estimation, or null when it isn't.
        public string? SkipReason => DownloadError ?? AnalysisError;
    }
}

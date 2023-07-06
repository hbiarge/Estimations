namespace Acheve.Application.ProcessManager.Handlers
{
    public class CaseImage
    {
        public const int MaxAnalisysWaits = 5;
        public static readonly TimeSpan AnalisysWaitTime = TimeSpan.FromSeconds(20);

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

        public int CurrentAnalisysWaits { get; set; }

        // An image is considered analyzed if
        // we couldn't download the image because an error (so we didn't send the image to process)
        // we have a ticket for the analysis (where the metadata is stored)
        // or if we couldn't analize the image because an error 
        public bool Analyzed => string.IsNullOrEmpty(DownloadError) == false
                                 || string.IsNullOrEmpty(AnalysisTicket) == false
                                 || string.IsNullOrEmpty(AnalysisError) == false;

        public bool AvailableToEstimate => string.IsNullOrEmpty(AnalysisTicket) == false;
    }
}
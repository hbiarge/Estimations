namespace Acheve.Application.ProcessManager.Handlers
{
    public class CaseImage
    {
        public const int MaxAnalisysWaits = 5;
        public static readonly TimeSpan AnalisysWaitTime = TimeSpan.FromSeconds(20);

        public int Id { get; set; }

        public required string Url { get; set; }

        public string? ImageTicket { get; set; }
        
        public string? DownloadError { get; set; }
        
        // An image is considered downloaded if
        // we have a ticket for the image (where the image is stored)
        // or if we couldn't download the image because an error 
        public bool Downloaded => string.IsNullOrEmpty(ImageTicket) == false 
                                  || string.IsNullOrEmpty(DownloadError) == false;

        public string? AnalisysTicket { get; set; }
        
        public string? AnalisysError { get; set; }

        public int CurrentAnalisysWaits { get; set; }

        // An image is considered analized if
        // we couldn't download the image because an error (so we didn't send the image to process)
        // we have a ticket for the analisys (where the metadata is stored)
        // or if we couldn't analize the image because an error 
        public bool Analized => string.IsNullOrEmpty(DownloadError) == false
                                 || string.IsNullOrEmpty(AnalisysTicket) == false
                                 || string.IsNullOrEmpty(AnalisysError) == false;

        public bool AvailableToEstimate => string.IsNullOrEmpty(AnalisysTicket) == false;
    }
}
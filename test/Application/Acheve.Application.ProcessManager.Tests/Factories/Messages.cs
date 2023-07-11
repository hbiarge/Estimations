using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Tests.Factories
{
    public static class Messages
    {
        public static EstimationRequested RequestEstimationMessage(Guid caseNumber)
        {
            return new EstimationRequested
            {
                CaseNumber = caseNumber,
                ClientId = "1234",
                CallbackUrl = "http://notification.com",
                ImageUrls = new[]
                {
                    "http://patata.com/image01.jpg"
                }
            };
        }

        public static ImageDownloaded ImageDownloadedMessage(Guid caseNumber, int imageId)
        {
            return new ImageDownloaded
            {
                CaseNumber = caseNumber,
                ImageId = imageId,
                ImageTicket = "imageTicket"
            };
        }
    }
}

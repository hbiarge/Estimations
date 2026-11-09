using Acheve.Application.ProcessManager.Handlers;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class CaseImageTests
    {
        [Fact]
        public void New_CaseImage_status_should_be_correct()
        {
            var sut = new CaseImage { 
                Id = 1, 
                Url = "https://images.com/test.png",
                Extension = "png"
            };

            sut.Downloaded.ShouldBeFalse();
            sut.Analyzed.ShouldBeFalse();
            sut.AvailableToEstimate.ShouldBeFalse();
        }

        [Fact]
        public void Successfully_downloaded_CaseImage_status_should_be_correct()
        {
            var sut = new CaseImage
            {
                Id = 1,
                Url = "https://images.com/test.png",
                Extension = "png",
                ImageTicket = "ticket"
            };

            sut.Downloaded.ShouldBeTrue();
            sut.Analyzed.ShouldBeFalse();
            sut.AvailableToEstimate.ShouldBeFalse();
        }

        [Fact]
        public void Download_error_CaseImage_status_should_be_correct()
        {
            var sut = new CaseImage
            {
                Id = 1,
                Url = "https://images.com/test.png",
                Extension = "png",
                DownloadError = "error"
            };

            sut.Downloaded.ShouldBeTrue();
            sut.Analyzed.ShouldBeTrue();
            sut.AvailableToEstimate.ShouldBeFalse();
        }

        [Fact]
        public void Successfully_Analyzed_CaseImage_status_should_be_correct()
        {
            var sut = new CaseImage
            {
                Id = 1,
                Url = "https://images.com/test.png",
                Extension = "png",
                ImageTicket = "ticket",
                AnalysisTicket = "ticket"
            };

            sut.Downloaded.ShouldBeTrue();
            sut.Analyzed.ShouldBeTrue();
            sut.AvailableToEstimate.ShouldBeTrue();
        }

        [Fact]
        public void Analysis_error_CaseImage_status_should_be_correct()
        {
            var sut = new CaseImage
            {
                Id = 1,
                Url = "https://images.com/test.png",
                Extension = "png",
                ImageTicket = "ticket",
                AnalysisError = "error"
            };

            sut.Downloaded.ShouldBeTrue();
            sut.Analyzed.ShouldBeTrue();
            sut.AvailableToEstimate.ShouldBeFalse();
        }
    }
}
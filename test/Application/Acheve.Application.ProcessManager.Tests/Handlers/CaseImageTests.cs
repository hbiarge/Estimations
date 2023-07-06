using Acheve.Application.ProcessManager.Handlers;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class CaseImageTests
    {
        [Fact]
        public void New_CaseImage_status_shoul_be_correct()
        {
            var sut = new CaseImage { 
                Id = 1, 
                Url = "https://images.com/test.png"
            };

            sut.Downloaded.Should().BeFalse();
            sut.Analyzed.Should().BeFalse();
            sut.AvailableToEstimate.Should().BeFalse();
        }

        [Fact]
        public void Successfully_downloaded_CaseImage_status_shoul_be_correct()
        {
            var sut = new CaseImage
            {
                Id = 1,
                Url = "https://images.com/test.png",
                ImageTicket = "ticket"
            };

            sut.Downloaded.Should().BeTrue();
            sut.Analyzed.Should().BeFalse();
            sut.AvailableToEstimate.Should().BeFalse();
        }

        [Fact]
        public void Download_error_CaseImage_status_shoul_be_correct()
        {
            var sut = new CaseImage
            {
                Id = 1,
                Url = "https://images.com/test.png",
                DownloadError = "error"
            };

            sut.Downloaded.Should().BeTrue();
            sut.Analyzed.Should().BeTrue();
            sut.AvailableToEstimate.Should().BeFalse();
        }

        [Fact]
        public void Successfully_Analyzed_CaseImage_status_shoul_be_correct()
        {
            var sut = new CaseImage
            {
                Id = 1,
                Url = "https://images.com/test.png",
                ImageTicket = "ticket",
                AnalysisTicket = "ticket"
            };

            sut.Downloaded.Should().BeTrue();
            sut.Analyzed.Should().BeTrue();
            sut.AvailableToEstimate.Should().BeTrue();
        }

        [Fact]
        public void Analysis_error_CaseImage_status_shoul_be_correct()
        {
            var sut = new CaseImage
            {
                Id = 1,
                Url = "https://images.com/test.png",
                ImageTicket = "ticket",
                AnalysisError = "error"
            };

            sut.Downloaded.Should().BeTrue();
            sut.Analyzed.Should().BeTrue();
            sut.AvailableToEstimate.Should().BeFalse();
        }
    }
}
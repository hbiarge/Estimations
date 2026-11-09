using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class CallbackSignerTests
    {
        private const string ApiUrl = "https://api.test";

        private readonly CallbackSigner _sut = new("test-signing-key-with-enough-length");
        private readonly Guid _caseNumber = Guid.NewGuid();

        [Fact]
        public void Image_analysis_url_signature_is_valid_for_its_case_and_image()
        {
            var signature = SignatureOf(_sut.ImageAnalysisUrl(ApiUrl, _caseNumber, 1));

            _sut.IsValidImageAnalysis(_caseNumber, 1, signature).ShouldBeTrue();
            _sut.IsValidImageAnalysis(_caseNumber, 2, signature).ShouldBeFalse();
            _sut.IsValidImageAnalysis(Guid.NewGuid(), 1, signature).ShouldBeFalse();
            _sut.IsValidEstimation(_caseNumber, signature).ShouldBeFalse();
        }

        [Fact]
        public void Estimation_url_signature_is_valid_for_its_case()
        {
            var url = _sut.EstimationUrl(ApiUrl, _caseNumber);
            var signature = SignatureOf(url);

            url.ShouldStartWith($"{ApiUrl}/ExternalEstimation/{_caseNumber:D}?signature=");
            _sut.IsValidEstimation(_caseNumber, signature).ShouldBeTrue();
            _sut.IsValidEstimation(Guid.NewGuid(), signature).ShouldBeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-signature")]
        public void Missing_or_wrong_signature_is_rejected(string? signature)
        {
            _sut.IsValidEstimation(_caseNumber, signature).ShouldBeFalse();
        }

        [Fact]
        public void Signature_from_another_key_is_rejected()
        {
            var other = new CallbackSigner("another-signing-key-with-enough-length");
            var signature = SignatureOf(other.EstimationUrl(ApiUrl, _caseNumber));

            _sut.IsValidEstimation(_caseNumber, signature).ShouldBeFalse();
        }

        private static string SignatureOf(string url) =>
            url[(url.IndexOf("signature=", StringComparison.Ordinal) + "signature=".Length)..];
    }
}

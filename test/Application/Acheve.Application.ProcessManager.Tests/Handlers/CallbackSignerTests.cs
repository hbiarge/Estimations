using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class CallbackSignerTests
    {
        private const string ApiUrl = "https://api.test";
        private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        private readonly CallbackSigner _sut = new("test-signing-key-with-enough-length");
        private readonly Guid _caseNumber = Guid.NewGuid();

        [Fact]
        public void Image_analysis_url_signature_is_valid_for_its_case_and_image()
        {
            var signature = SignatureOf(_sut.ImageAnalysisUrl(ApiUrl, _caseNumber, 1, traceParent: null));

            _sut.IsValidImageAnalysis(_caseNumber, 1, null, signature).ShouldBeTrue();
            _sut.IsValidImageAnalysis(_caseNumber, 2, null, signature).ShouldBeFalse();
            _sut.IsValidImageAnalysis(Guid.NewGuid(), 1, null, signature).ShouldBeFalse();
            _sut.IsValidEstimation(_caseNumber, null, signature).ShouldBeFalse();
        }

        [Fact]
        public void Estimation_url_signature_is_valid_for_its_case()
        {
            var url = _sut.EstimationUrl(ApiUrl, _caseNumber, traceParent: null);
            var signature = SignatureOf(url);

            url.ShouldStartWith($"{ApiUrl}/ExternalEstimation/{_caseNumber:D}?signature=");
            _sut.IsValidEstimation(_caseNumber, null, signature).ShouldBeTrue();
            _sut.IsValidEstimation(Guid.NewGuid(), null, signature).ShouldBeFalse();
        }

        [Fact]
        public void Url_carries_the_trace_context_before_the_signature()
        {
            var url = _sut.ImageAnalysisUrl(ApiUrl, _caseNumber, 1, TraceParent);

            url.ShouldStartWith(
                $"{ApiUrl}/ExternalImageProcess/{_caseNumber:D}/images/1?traceparent={TraceParent}&signature=");
        }

        [Fact]
        public void Signature_covers_the_trace_context()
        {
            var imageSignature = SignatureOf(_sut.ImageAnalysisUrl(ApiUrl, _caseNumber, 1, TraceParent));
            var estimationSignature = SignatureOf(_sut.EstimationUrl(ApiUrl, _caseNumber, TraceParent));
            const string otherTrace = "00-11111111111111111111111111111111-2222222222222222-01";

            _sut.IsValidImageAnalysis(_caseNumber, 1, TraceParent, imageSignature).ShouldBeTrue();
            _sut.IsValidImageAnalysis(_caseNumber, 1, otherTrace, imageSignature).ShouldBeFalse();
            _sut.IsValidImageAnalysis(_caseNumber, 1, null, imageSignature).ShouldBeFalse();

            _sut.IsValidEstimation(_caseNumber, TraceParent, estimationSignature).ShouldBeTrue();
            _sut.IsValidEstimation(_caseNumber, otherTrace, estimationSignature).ShouldBeFalse();
            _sut.IsValidEstimation(_caseNumber, null, estimationSignature).ShouldBeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-signature")]
        public void Missing_or_wrong_signature_is_rejected(string? signature)
        {
            _sut.IsValidEstimation(_caseNumber, null, signature).ShouldBeFalse();
        }

        [Fact]
        public void Signature_from_another_key_is_rejected()
        {
            var other = new CallbackSigner("another-signing-key-with-enough-length");
            var signature = SignatureOf(other.EstimationUrl(ApiUrl, _caseNumber, traceParent: null));

            _sut.IsValidEstimation(_caseNumber, null, signature).ShouldBeFalse();
        }

        private static string SignatureOf(string url) =>
            url[(url.IndexOf("signature=", StringComparison.Ordinal) + "signature=".Length)..];
    }
}

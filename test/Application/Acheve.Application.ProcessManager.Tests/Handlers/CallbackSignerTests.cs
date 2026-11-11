using Acheve.Common.Shared;
using Microsoft.Extensions.Time.Testing;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class CallbackSignerTests
    {
        private const string ApiUrl = "https://api.test";
        private const string TraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        private readonly CallbackSigner _sut;
        private readonly Guid _caseNumber = Guid.NewGuid();

        public CallbackSignerTests()
        {
            _sut = new CallbackSigner("test-signing-key-with-enough-length", _time, TimeSpan.FromMinutes(30));
        }

        [Fact]
        public void Image_analysis_url_signature_is_valid_for_its_case_and_image()
        {
            var (expires, signature) = ParamsOf(_sut.ImageAnalysisUrl(ApiUrl, _caseNumber, 1, traceParent: null));

            _sut.IsValidImageAnalysis(_caseNumber, 1, null, expires, signature).ShouldBeTrue();
            _sut.IsValidImageAnalysis(_caseNumber, 2, null, expires, signature).ShouldBeFalse();
            _sut.IsValidImageAnalysis(Guid.NewGuid(), 1, null, expires, signature).ShouldBeFalse();
            _sut.IsValidEstimation(_caseNumber, null, expires, signature).ShouldBeFalse();
        }

        [Fact]
        public void Estimation_url_signature_is_valid_for_its_case()
        {
            var url = _sut.EstimationUrl(ApiUrl, _caseNumber, traceParent: null);
            var (expires, signature) = ParamsOf(url);

            url.ShouldStartWith($"{ApiUrl}/callbacks/estimation/{_caseNumber:D}?expires=");
            _sut.IsValidEstimation(_caseNumber, null, expires, signature).ShouldBeTrue();
            _sut.IsValidEstimation(Guid.NewGuid(), null, expires, signature).ShouldBeFalse();
        }

        [Fact]
        public void Url_carries_the_trace_context_before_the_expiry_and_the_signature()
        {
            var url = _sut.ImageAnalysisUrl(ApiUrl, _caseNumber, 1, TraceParent);

            url.ShouldStartWith(
                $"{ApiUrl}/callbacks/image-analysis/{_caseNumber:D}/images/1?traceparent={TraceParent}&expires=");
            url.ShouldContain("&signature=");
        }

        [Fact]
        public void Signature_covers_the_trace_context()
        {
            var (expires, imageSignature) = ParamsOf(_sut.ImageAnalysisUrl(ApiUrl, _caseNumber, 1, TraceParent));
            var (estimationExpires, estimationSignature) = ParamsOf(_sut.EstimationUrl(ApiUrl, _caseNumber, TraceParent));
            const string otherTrace = "00-11111111111111111111111111111111-2222222222222222-01";

            _sut.IsValidImageAnalysis(_caseNumber, 1, TraceParent, expires, imageSignature).ShouldBeTrue();
            _sut.IsValidImageAnalysis(_caseNumber, 1, otherTrace, expires, imageSignature).ShouldBeFalse();
            _sut.IsValidImageAnalysis(_caseNumber, 1, null, expires, imageSignature).ShouldBeFalse();

            _sut.IsValidEstimation(_caseNumber, TraceParent, estimationExpires, estimationSignature).ShouldBeTrue();
            _sut.IsValidEstimation(_caseNumber, otherTrace, estimationExpires, estimationSignature).ShouldBeFalse();
            _sut.IsValidEstimation(_caseNumber, null, estimationExpires, estimationSignature).ShouldBeFalse();
        }

        [Fact]
        public void Url_stops_working_after_it_expires()
        {
            var (expires, signature) = ParamsOf(_sut.EstimationUrl(ApiUrl, _caseNumber, traceParent: null));

            _time.Advance(TimeSpan.FromMinutes(29));
            _sut.IsValidEstimation(_caseNumber, null, expires, signature).ShouldBeTrue();

            _time.Advance(TimeSpan.FromMinutes(2));
            _sut.IsValidEstimation(_caseNumber, null, expires, signature).ShouldBeFalse();
        }

        [Fact]
        public void Expiry_can_not_be_extended_without_the_key()
        {
            var (expires, signature) = ParamsOf(_sut.EstimationUrl(ApiUrl, _caseNumber, traceParent: null));

            _sut.IsValidEstimation(_caseNumber, null, expires + 3600, signature).ShouldBeFalse();
        }

        [Fact]
        public void Url_without_an_expiry_is_rejected()
        {
            var (_, signature) = ParamsOf(_sut.EstimationUrl(ApiUrl, _caseNumber, traceParent: null));

            _sut.IsValidEstimation(_caseNumber, null, expires: null, signature).ShouldBeFalse();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-signature")]
        public void Missing_or_wrong_signature_is_rejected(string? signature)
        {
            _sut.IsValidEstimation(_caseNumber, null, _time.GetUtcNow().ToUnixTimeSeconds() + 60, signature).ShouldBeFalse();
        }

        [Fact]
        public void Signature_from_another_key_is_rejected()
        {
            var other = new CallbackSigner("another-signing-key-with-enough-length", _time);
            var (expires, signature) = ParamsOf(other.EstimationUrl(ApiUrl, _caseNumber, traceParent: null));

            _sut.IsValidEstimation(_caseNumber, null, expires, signature).ShouldBeFalse();
        }

        private static (long Expires, string Signature) ParamsOf(string url)
        {
            var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);

            return (long.Parse(query["expires"]!), query["signature"]!);
        }
    }
}

using System.Diagnostics;
using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public sealed class CallbackTracingTests : IDisposable
    {
        private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
        private const string ParentSpanId = "b7ad6b7169203331";
        private const string TraceParent = $"00-{TraceId}-{ParentSpanId}-01";

        // A unique source name per test class instance, so parallel tests don't see each other's spans.
        private readonly string _sourceName = $"callback-tracing-tests-{Guid.NewGuid():N}";
        private readonly ActivityListener _listener;
        private readonly CallbackTracing _sut;
        private readonly Guid _caseNumber = Guid.NewGuid();

        public CallbackTracingTests()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == _sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };
            ActivitySource.AddActivityListener(_listener);
            _sut = new CallbackTracing(_sourceName);
        }

        [Fact]
        public void Callback_span_continues_the_trace_from_the_url()
        {
            using var activity = _sut.StartCallbackActivity("callback", TraceParent, _caseNumber, imageId: 3);

            activity.ShouldNotBeNull();
            activity.TraceId.ToHexString().ShouldBe(TraceId);
            activity.ParentSpanId.ToHexString().ShouldBe(ParentSpanId);
            activity.GetTagItem(CallbackTracing.CaseNumberTag).ShouldBe(_caseNumber.ToString("D"));
            activity.GetTagItem(CallbackTracing.ImageIdTag).ShouldBe(3);
            Activity.Current.ShouldBe(activity);
        }

        [Fact]
        public void Callback_span_links_to_the_incoming_request_span()
        {
            using var requestSource = new ActivitySource(_sourceName);
            using var request = requestSource.StartActivity("request");
            request.ShouldNotBeNull();

            using var activity = _sut.StartCallbackActivity("callback", TraceParent, _caseNumber);

            activity.ShouldNotBeNull();
            activity.Links.ShouldHaveSingleItem().Context.SpanId.ShouldBe(request.SpanId);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-traceparent")]
        public void No_span_without_a_valid_trace_context(string? traceParent)
        {
            _sut.StartCallbackActivity("callback", traceParent, _caseNumber).ShouldBeNull();
        }

        [Fact]
        public void Current_trace_parent_is_the_current_span_id()
        {
            using var source = new ActivitySource(_sourceName);
            using var activity = source.StartActivity("handler");
            activity.ShouldNotBeNull();

            CallbackTracing.CurrentTraceParent.ShouldBe(activity.Id);
            CallbackTracing.CurrentTraceParent.ShouldStartWith($"00-{activity.TraceId.ToHexString()}-");
        }

        public void Dispose()
        {
            _sut.Dispose();
            _listener.Dispose();
        }
    }
}

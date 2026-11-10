using System.Diagnostics;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// Carries a case's trace across the external systems' callbacks.
    /// External systems don't send our trace context back, so we put it in the callback URL
    /// (see <see cref="CallbackSigner"/>) and continue the trace from there when they call back.
    /// </summary>
    public sealed class CallbackTracing : IDisposable
    {
        public const string CaseNumberTag = "estimation.case_number";
        public const string ImageIdTag = "estimation.image_id";

        private readonly ActivitySource _source;

        /// <param name="sourceName">Must be registered with OpenTelemetry (the service defaults register the application name).</param>
        public CallbackTracing(string sourceName)
        {
            _source = new ActivitySource(sourceName);
        }

        /// <summary>The W3C traceparent of the current activity, to put in a callback URL.</summary>
        public static string? CurrentTraceParent =>
            Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;

        /// <summary>
        /// Starts a span in the case's trace, as a child of the span that built the callback URL,
        /// linked to the incoming callback request's own span.
        /// Returns null when there is no valid trace context or nobody listens; the request's span stays current then.
        /// </summary>
        public Activity? StartCallbackActivity(string name, string? traceParent, Guid caseNumber, int? imageId = null)
        {
            if (!ActivityContext.TryParse(traceParent, traceState: null, isRemote: true, out var parent))
            {
                return null;
            }

            ActivityLink[]? links = Activity.Current is { } request
                ? [new ActivityLink(request.Context)]
                : null;

            var activity = _source.StartActivity(name, ActivityKind.Internal, parent, tags: null, links);

            activity?.SetTag(CaseNumberTag, caseNumber.ToString("D"));

            if (imageId is not null)
            {
                activity?.SetTag(ImageIdTag, imageId.Value);
            }

            return activity;
        }

        public void Dispose() => _source.Dispose();
    }
}

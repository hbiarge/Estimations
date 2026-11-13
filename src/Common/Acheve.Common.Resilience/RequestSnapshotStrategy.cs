using Polly;
using Polly.Hedging;

namespace Acheve.Common.Resilience
{
    /// <summary>
    /// Copies the request before hedging starts, so every hedged attempt can send its own copy.
    /// Taking the copy up front, and not when the hedge fires, avoids reading the first request
    /// while the handlers below are still changing it.
    /// </summary>
    internal sealed class RequestSnapshotStrategy : ResilienceStrategy<HttpResponseMessage>
    {
        private static readonly ResiliencePropertyKey<RequestSnapshot> SnapshotKey = new("Acheve.RequestSnapshot");

        protected override async ValueTask<Outcome<HttpResponseMessage>> ExecuteCore<TState>(
            Func<ResilienceContext, TState, ValueTask<Outcome<HttpResponseMessage>>> callback,
            ResilienceContext context,
            TState state)
        {
            if (context.GetRequestMessage() is { } request)
            {
                context.Properties.Set(SnapshotKey, await RequestSnapshot.Create(request, context.CancellationToken));
            }

            return await callback(context, state);
        }

        /// <summary>The hedging action generator: sends a fresh copy of the request on every hedged attempt.</summary>
        public static Func<ValueTask<Outcome<HttpResponseMessage>>>? HedgedAction(HedgingActionGeneratorArguments<HttpResponseMessage> args)
        {
            if (!args.PrimaryContext.Properties.TryGetValue(SnapshotKey, out var snapshot))
            {
                return null;
            }

            return () =>
            {
                args.ActionContext.SetRequestMessage(snapshot.CreateRequest());
                return args.Callback(args.ActionContext);
            };
        }

        private sealed class RequestSnapshot
        {
            // A copy of these would put every hedged attempt in the first attempt's span.
            private static readonly HashSet<string> TraceHeaders = new(StringComparer.OrdinalIgnoreCase) { "traceparent", "tracestate", "baggage" };

            private readonly HttpRequestMessage _request;
            private readonly byte[]? _content;

            private RequestSnapshot(HttpRequestMessage request, byte[]? content)
            {
                _request = request;
                _content = content;
            }

            public static async Task<RequestSnapshot> Create(HttpRequestMessage request, CancellationToken cancellationToken) =>
                new(request, request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken));

            public HttpRequestMessage CreateRequest()
            {
                var copy = new HttpRequestMessage(_request.Method, _request.RequestUri)
                {
                    Version = _request.Version,
                    VersionPolicy = _request.VersionPolicy
                };

                foreach (var (name, values) in _request.Headers.Where(h => !TraceHeaders.Contains(h.Key)))
                {
                    copy.Headers.TryAddWithoutValidation(name, values);
                }

                // The options carry, among others, the peer.service the HttpClient instrumentation reads.
                foreach (var (key, value) in _request.Options)
                {
                    copy.Options.Set(new HttpRequestOptionsKey<object?>(key), value);
                }

                if (_content is not null)
                {
                    copy.Content = new ByteArrayContent(_content);

                    foreach (var (name, values) in _request.Content!.Headers)
                    {
                        copy.Content.Headers.TryAddWithoutValidation(name, values);
                    }
                }

                return copy;
            }
        }
    }

    internal sealed class RequestSnapshotStrategyOptions : ResilienceStrategyOptions
    {
        public RequestSnapshotStrategyOptions() => Name = "RequestSnapshot";
    }
}

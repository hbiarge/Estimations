using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Names the system an HttpClient calls in its spans (peer.service). The external systems don't join our traces,
    /// so Tempo can't pair our call with their span; with peer.service it still draws the edge to them by name.
    /// </summary>
    public static class PeerService
    {
        private const string TagName = "peer.service";

        private static readonly HttpRequestOptionsKey<string> OptionsKey = new(TagName);

        public static IHttpClientBuilder WithPeerService(this IHttpClientBuilder builder, string peerService) =>
            builder.AddHttpMessageHandler(() => new PeerServiceHandler(peerService));

        // Called by the HttpClient instrumentation for every outgoing request.
        internal static void Enrich(System.Diagnostics.Activity activity, HttpRequestMessage request)
        {
            if (request.Options.TryGetValue(OptionsKey, out var peerService))
            {
                activity.SetTag(TagName, peerService);
            }
        }

        private sealed class PeerServiceHandler(string peerService) : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.Options.Set(OptionsKey, peerService);
                return base.SendAsync(request, cancellationToken);
            }
        }
    }
}

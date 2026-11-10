using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>Gets a client credentials token and reuses it until a minute before it expires.</summary>
    internal sealed class TokenProvider(IHttpClientFactory httpClientFactory, IOptions<TrafficOptions> options, TimeProvider time)
    {
        public const string HttpClientName = "identity";

        private readonly SemaphoreSlim _lock = new(1, 1);
        private string? _token;
        private DateTimeOffset _renewAt;

        public async Task<string> GetAsync(CancellationToken cancellationToken)
        {
            if (_token is not null && time.GetUtcNow() < _renewAt)
            {
                return _token;
            }

            await _lock.WaitAsync(cancellationToken);

            try
            {
                if (_token is not null && time.GetUtcNow() < _renewAt)
                {
                    return _token;
                }

                var settings = options.Value.Token;

                using var http = httpClientFactory.CreateClient(HttpClientName);
                using var response = await http.PostAsync(
                    new Uri(new Uri(settings.Authority), "connect/token"),
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["client_id"] = settings.ClientId,
                        ["client_secret"] = settings.ClientSecret,
                        ["scope"] = settings.Scope
                    }),
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                    ?? throw new InvalidOperationException("Empty token response.");

                _token = token.AccessToken;
                _renewAt = time.GetUtcNow() + TimeSpan.FromSeconds(Math.Max(0, token.ExpiresIn - 60));

                return _token;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Forgets the token, for example after the Api answered 401.</summary>
        public void Invalidate() => _token = null;

        private sealed record TokenResponse(
            [property: JsonPropertyName("access_token")] string AccessToken,
            [property: JsonPropertyName("expires_in")] int ExpiresIn);
    }
}

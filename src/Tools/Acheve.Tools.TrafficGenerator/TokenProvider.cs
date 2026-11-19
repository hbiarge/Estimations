using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>Gets a client credentials token for each customer and reuses it until a minute before it expires.</summary>
    internal sealed class TokenProvider(IHttpClientFactory httpClientFactory, IOptions<TrafficOptions> options, TimeProvider time) : IDisposable
    {
        public const string HttpClientName = "identity";

        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly Dictionary<string, (string Token, DateTimeOffset RenewAt)> _tokens = [];

        public async Task<string> GetAsync(CustomerOptions customer, CancellationToken cancellationToken)
        {
            if (TryGetCached(customer.Name, out var cached))
            {
                return cached;
            }

            await _lock.WaitAsync(cancellationToken);

            try
            {
                if (TryGetCached(customer.Name, out cached))
                {
                    return cached;
                }

                using var http = httpClientFactory.CreateClient(HttpClientName);
                using var response = await http.PostAsync(
                    new Uri(options.Value.TokenEndpoint),
                    new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["client_id"] = customer.ClientId,
                        ["client_secret"] = customer.ClientSecret
                    }),
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                    ?? throw new InvalidOperationException("Empty token response.");

                var renewAt = time.GetUtcNow() + TimeSpan.FromSeconds(Math.Max(0, token.ExpiresIn - 60));

                lock (_tokens)
                {
                    _tokens[customer.Name] = (token.AccessToken, renewAt);
                }

                return token.AccessToken;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Forgets the customer's token, for example after the Api answered 401.</summary>
        public void Invalidate(string customerName)
        {
            lock (_tokens)
            {
                _tokens.Remove(customerName);
            }
        }

        private bool TryGetCached(string customerName, out string token)
        {
            lock (_tokens)
            {
                if (_tokens.TryGetValue(customerName, out var entry) && time.GetUtcNow() < entry.RenewAt)
                {
                    token = entry.Token;
                    return true;
                }
            }

            token = "";
            return false;
        }

        private sealed record TokenResponse(
            [property: JsonPropertyName("access_token")] string AccessToken,
            [property: JsonPropertyName("expires_in")] int ExpiresIn);

        public void Dispose() => _lock.Dispose();
    }
}

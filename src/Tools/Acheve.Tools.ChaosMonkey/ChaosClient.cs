namespace Acheve.Tools.ChaosMonkey
{
    internal interface IChaosClient
    {
        /// <summary>Switches a service's chaos scenario. False when the service couldn't be reached or refused it.</summary>
        Task<bool> SetScenarioAsync(string target, string scenario, CancellationToken cancellationToken);

        /// <summary>Switches a caller service's resilience profile. False when the service couldn't be reached or refused it.</summary>
        Task<bool> SetProfileAsync(string caller, string profile, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Calls POST /chaos/scenarios/{scenario} or POST /resilience/profiles/{profile} on the service,
    /// found through service discovery.
    /// </summary>
    internal sealed class ChaosClient(IHttpClientFactory httpClientFactory) : IChaosClient
    {
        public const string HttpClientName = "chaos";

        public Task<bool> SetScenarioAsync(string target, string scenario, CancellationToken cancellationToken) =>
            PostAsync(new Uri($"https+http://{target}/chaos/scenarios/{scenario}"), cancellationToken);

        public Task<bool> SetProfileAsync(string caller, string profile, CancellationToken cancellationToken) =>
            PostAsync(new Uri($"https+http://{caller}/resilience/profiles/{profile}"), cancellationToken);

        private async Task<bool> PostAsync(Uri uri, CancellationToken cancellationToken)
        {
            using var http = httpClientFactory.CreateClient(HttpClientName);

            try
            {
                using var response = await http.PostAsync(uri, content: null, cancellationToken);

                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }
    }
}

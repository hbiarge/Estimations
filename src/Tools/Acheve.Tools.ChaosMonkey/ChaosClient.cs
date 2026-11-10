namespace Acheve.Tools.ChaosMonkey
{
    internal interface IChaosClient
    {
        /// <summary>Switches a service's chaos scenario. False when the service couldn't be reached or refused it.</summary>
        Task<bool> SetScenarioAsync(string target, string scenario, CancellationToken cancellationToken);
    }

    /// <summary>Calls POST /chaos/scenarios/{scenario} on the target, found through service discovery.</summary>
    internal sealed class ChaosClient(IHttpClientFactory httpClientFactory) : IChaosClient
    {
        public const string HttpClientName = "chaos";

        public async Task<bool> SetScenarioAsync(string target, string scenario, CancellationToken cancellationToken)
        {
            using var http = httpClientFactory.CreateClient(HttpClientName);

            try
            {
                using var response = await http.PostAsync(
                    new Uri($"https+http://{target}/chaos/scenarios/{scenario}"),
                    content: null,
                    cancellationToken);

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

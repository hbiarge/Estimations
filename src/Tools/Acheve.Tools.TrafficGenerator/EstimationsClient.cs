using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>The Api's client endpoints.</summary>
    internal interface IEstimationsClient
    {
        /// <summary>Starts a case. Returns its ticket, or null when the Api didn't accept it.</summary>
        Task<string?> StartAsync(NewEstimationRequest request, CancellationToken cancellationToken);

        /// <summary>The case's current state, or null when it couldn't be read this time.</summary>
        Task<string?> GetStateAsync(string ticket, CancellationToken cancellationToken);
    }

    internal sealed class EstimationsClient(IHttpClientFactory httpClientFactory, TokenProvider tokens) : IEstimationsClient
    {
        public const string HttpClientName = "api";

        public async Task<string?> StartAsync(NewEstimationRequest request, CancellationToken cancellationToken)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/estimations")
            {
                Content = JsonContent.Create(request)
            };

            using var response = await SendAsync(message, cancellationToken);

            if (response?.StatusCode != HttpStatusCode.Accepted)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<NewEstimationResponse>(cancellationToken);
            return body?.Token;
        }

        public async Task<string?> GetStateAsync(string ticket, CancellationToken cancellationToken)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"/estimations/{ticket}");
            using var response = await SendAsync(message, cancellationToken);

            if (response is not { IsSuccessStatusCode: true })
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<EstimationStateResponse>(cancellationToken);
            return body?.State;
        }

        // Null when the call itself failed (connection refused, timeout after the retries).
        private async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(cancellationToken));

            try
            {
                using var http = httpClientFactory.CreateClient(HttpClientName);
                var response = await http.SendAsync(message, cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    tokens.Invalidate();
                }

                return response;
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        private sealed record NewEstimationResponse(string Token, string OperationId);

        private sealed record EstimationStateResponse(string Ticket, string State);
    }
}

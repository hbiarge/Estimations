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

        /// <summary>
        /// The case's current status, or null when it couldn't be read this time
        /// (also right after the start, while the Api doesn't know the case yet and answers 404).
        /// </summary>
        Task<CaseStatus?> GetStateAsync(string ticket, CancellationToken cancellationToken);
    }

    /// <summary>What GET /estimations/{ticket} returns that the generator uses.</summary>
    /// <param name="State">The name of the case's state.</param>
    /// <param name="Result">"Success" or "Failure" once the case has an estimation or has failed; null before.</param>
    internal sealed record CaseStatus(string State, string? Result = null);

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

        public async Task<CaseStatus?> GetStateAsync(string ticket, CancellationToken cancellationToken)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, $"/estimations/{ticket}");
            using var response = await SendAsync(message, cancellationToken);

            if (response is not { IsSuccessStatusCode: true })
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CaseStatus>(cancellationToken);
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
    }
}

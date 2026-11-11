using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Bus;

namespace Acheve.Application.CallbackNotifier
{
    /// <summary>
    /// Posts a case's result to the client's callback URL and tells the saga how it went.
    /// </summary>
    public sealed class ClientNotifier(
        IBus bus,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ClientNotifier> logger)
    {
        // Null when no key is set: the notifications then go unsigned.
        private readonly NotificationSigner? _signer = configuration.GetNotificationSigner();

        /// <param name="buildPayload">
        /// Builds the body. It runs inside the error handling, so a payload that can't be built
        /// (a missing attachment) is reported to the saga instead of failing the message.
        /// </param>
        public async Task Notify(Guid caseNumber, string callbackUrl, int attempt, Func<Task<object>> buildPayload)
        {
            try
            {
                var body = JsonSerializer.Serialize(await buildPayload());

                using var request = new HttpRequestMessage(HttpMethod.Post, callbackUrl)
                {
                    Content = new StringContent(body, Encoding.UTF8, MediaTypeNames.Application.Json)
                };

                // The same for every attempt: HTTP retries, redelivered messages and the saga's own retries
                // can deliver the result more than once, and the client can use the key to act on it only once.
                request.Headers.Add(Idempotency.HeaderName, caseNumber.ToString("D"));

                if (_signer is not null)
                {
                    var (timestamp, signature) = _signer.Sign(body);
                    request.Headers.Add(NotificationSigner.TimestampHeader, timestamp);
                    request.Headers.Add(NotificationSigner.SignatureHeader, signature);
                }

                var client = httpClientFactory.CreateClient("notifications");
                using var response = await client.SendAsync(request);

                // An error response from the client is a failed notification, not a completed one.
                response.EnsureSuccessStatusCode();
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    "Case number {caseNumber} notification error (attempt {attempt}). {notificationError}",
                    caseNumber,
                    attempt,
                    e.Message);

                await bus.Send(new UnableToNotify
                {
                    CaseNumber = caseNumber,
                    Attempt = attempt,
                    Error = e.Message
                });

                return;
            }

            logger.LogInformation(
                "Case {caseNumber} notified successfully (attempt {attempt}).",
                caseNumber,
                attempt);

            await bus.Send(new NotificationCompleted
            {
                CaseNumber = caseNumber,
                Attempt = attempt
            });
        }
    }
}

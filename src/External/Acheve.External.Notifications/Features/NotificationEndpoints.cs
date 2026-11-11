using System.Text.Json;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Acheve.External.Notifications.Features
{
    /// <summary>The application's signer, or null when this client doesn't check signatures.</summary>
    public sealed record NotificationVerifier(NotificationSigner? Signer);

    public static class NotificationEndpoints
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public static IEndpointRouteBuilder MapNotification(this IEndpointRouteBuilder app)
        {
            app.MapPost("/Notification", Notification);

            return app;
        }

        private static async Task<Results<NoContent, BadRequest<string>, UnauthorizedHttpResult>> Notification(
            HttpRequest request,
            [FromHeader(Name = NotificationSigner.TimestampHeader)] string? timestamp,
            [FromHeader(Name = NotificationSigner.SignatureHeader)] string? signature,
            [FromHeader(Name = Idempotency.HeaderName)] string? idempotencyKey,
            NotificationVerifier verifier,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(NotificationEndpoints));

            // The signature covers the body exactly as it was sent, so read it before parsing it.
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync();

            if (verifier.Signer is { } signer && !signer.IsValid(body, timestamp, signature))
            {
                logger.LogWarning("Rejected a notification: missing, wrong or old signature.");

                return TypedResults.Unauthorized();
            }

            NotificationModel? model;

            try
            {
                model = JsonSerializer.Deserialize<NotificationModel>(body, JsonOptions);
            }
            catch (JsonException)
            {
                model = null;
            }

            if (model is null || string.IsNullOrEmpty(model.CaseNumber) || string.IsNullOrEmpty(model.Result))
            {
                return TypedResults.BadRequest("CaseNumber and Result are required.");
            }

            // Simulate some time to process
            await Task.Delay(2000);

            // A real client would use the idempotency key to act only once on a notification delivered twice.
            logger.LogInformation(
                "Received case completion notification. CaseNumber: {caseNumber} with result {result}. Skipped images: {skippedImages}. Idempotency key: {idempotencyKey}",
                model.CaseNumber,
                model.Result,
                model.SkippedImages.Count,
                idempotencyKey);

            return TypedResults.NoContent();
        }
    }
}

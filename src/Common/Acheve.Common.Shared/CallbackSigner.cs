using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// Signs the callback URLs we give to the external systems, and checks the signature when they call back.
    /// Only a caller that received the URL from us can report a result for that case (and image).
    /// The URL can also carry the W3C trace context of the case, so the callback continues the case's trace.
    /// It is part of the signature, so nobody can attach a callback to another trace.
    /// </summary>
    public sealed class CallbackSigner
    {
        public const string SignatureParameter = "signature";
        public const string TraceParentParameter = "traceparent";

        private readonly byte[] _key;

        public CallbackSigner(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("The callback signing key can't be empty.", nameof(key));
            }

            _key = Encoding.UTF8.GetBytes(key);
        }

        public static CallbackSigner FromConfiguration(IConfiguration configuration) =>
            new(configuration.GetCallbackSigningKey());

        public string ImageAnalysisUrl(string apiUrl, Guid caseNumber, int imageId, string? traceParent) =>
            BuildUrl(
                $"{apiUrl}/ExternalImageProcess/{caseNumber:D}/images/{imageId:G}",
                ImageAnalysisResource(caseNumber, imageId),
                traceParent);

        public string EstimationUrl(string apiUrl, Guid caseNumber, string? traceParent) =>
            BuildUrl(
                $"{apiUrl}/ExternalEstimation/{caseNumber:D}",
                EstimationResource(caseNumber),
                traceParent);

        public bool IsValidImageAnalysis(Guid caseNumber, int imageId, string? traceParent, string? signature) =>
            IsValid(ImageAnalysisResource(caseNumber, imageId), traceParent, signature);

        public bool IsValidEstimation(Guid caseNumber, string? traceParent, string? signature) =>
            IsValid(EstimationResource(caseNumber), traceParent, signature);

        private static string ImageAnalysisResource(Guid caseNumber, int imageId) =>
            $"image-analysis:{caseNumber:D}:{imageId:G}";

        private static string EstimationResource(Guid caseNumber) =>
            $"estimation:{caseNumber:D}";

        private string BuildUrl(string path, string resource, string? traceParent)
        {
            var query = new StringBuilder("?");

            if (!string.IsNullOrEmpty(traceParent))
            {
                query.Append(TraceParentParameter).Append('=').Append(Uri.EscapeDataString(traceParent)).Append('&');
            }

            query.Append(SignatureParameter).Append('=').Append(Sign(resource, traceParent));

            return path + query;
        }

        // A traceparent never contains a newline, so the signed text is unambiguous.
        private string Sign(string resource, string? traceParent) =>
            Convert.ToHexStringLower(HMACSHA256.HashData(
                _key,
                Encoding.UTF8.GetBytes($"{resource}\n{traceParent}")));

        private bool IsValid(string resource, string? traceParent, string? signature)
        {
            if (string.IsNullOrEmpty(signature))
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Sign(resource, traceParent)),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// Signs the callback URLs we give to the external systems, and checks the signature when they call back.
    /// Only a caller that received the URL from us can report a result for that case (and image).
    /// </summary>
    public sealed class CallbackSigner
    {
        public const string SignatureParameter = "signature";

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

        public string ImageAnalysisUrl(string apiUrl, Guid caseNumber, int imageId) =>
            $"{apiUrl}/ExternalImageProcess/{caseNumber:D}/images/{imageId:G}" +
            $"?{SignatureParameter}={Sign(ImageAnalysisResource(caseNumber, imageId))}";

        public string EstimationUrl(string apiUrl, Guid caseNumber) =>
            $"{apiUrl}/ExternalEstimation/{caseNumber:D}" +
            $"?{SignatureParameter}={Sign(EstimationResource(caseNumber))}";

        public bool IsValidImageAnalysis(Guid caseNumber, int imageId, string? signature) =>
            IsValid(ImageAnalysisResource(caseNumber, imageId), signature);

        public bool IsValidEstimation(Guid caseNumber, string? signature) =>
            IsValid(EstimationResource(caseNumber), signature);

        private static string ImageAnalysisResource(Guid caseNumber, int imageId) =>
            $"image-analysis:{caseNumber:D}:{imageId:G}";

        private static string EstimationResource(Guid caseNumber) =>
            $"estimation:{caseNumber:D}";

        private string Sign(string resource) =>
            Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(resource)));

        private bool IsValid(string resource, string? signature)
        {
            if (string.IsNullOrEmpty(signature))
            {
                return false;
            }

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Sign(resource)),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));
        }
    }
}

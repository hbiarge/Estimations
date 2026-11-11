using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Acheve.Common.Shared
{
    /// <summary>
    /// Signs the callback URLs we give to the external systems, and checks the signature when they call back.
    /// Only a caller that received the URL from us can report a result for that case (and image),
    /// and only until the URL expires.
    /// The URL can also carry the W3C trace context of the case, so the callback continues the case's trace.
    /// It is part of the signature, so nobody can attach a callback to another trace.
    /// </summary>
    public sealed class CallbackSigner
    {
        public const string SignatureParameter = "signature";
        public const string TraceParentParameter = "traceparent";
        public const string ExpiresParameter = "expires";

        // Longer than any wait for a callback (about 4 minutes), with room for a slow external system.
        public static readonly TimeSpan DefaultValidity = TimeSpan.FromMinutes(30);

        private readonly byte[] _key;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _validity;

        public CallbackSigner(string key, TimeProvider? timeProvider = null, TimeSpan? validity = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("The callback signing key can't be empty.", nameof(key));
            }

            _key = Encoding.UTF8.GetBytes(key);
            _timeProvider = timeProvider ?? TimeProvider.System;
            _validity = validity ?? DefaultValidity;
        }

        public static CallbackSigner FromConfiguration(IConfiguration configuration) =>
            new(configuration.GetCallbackSigningKey());

        public string ImageAnalysisUrl(string apiUrl, Guid caseNumber, int imageId, string? traceParent) =>
            BuildUrl(
                $"{apiUrl}/callbacks/image-analysis/{caseNumber:D}/images/{imageId:G}",
                ImageAnalysisResource(caseNumber, imageId),
                traceParent);

        public string EstimationUrl(string apiUrl, Guid caseNumber, string? traceParent) =>
            BuildUrl(
                $"{apiUrl}/callbacks/estimation/{caseNumber:D}",
                EstimationResource(caseNumber),
                traceParent);

        /// <param name="expires">The expires parameter of the URL: seconds since the Unix epoch.</param>
        public bool IsValidImageAnalysis(Guid caseNumber, int imageId, string? traceParent, long? expires, string? signature) =>
            IsValid(ImageAnalysisResource(caseNumber, imageId), traceParent, expires, signature);

        /// <param name="expires">The expires parameter of the URL: seconds since the Unix epoch.</param>
        public bool IsValidEstimation(Guid caseNumber, string? traceParent, long? expires, string? signature) =>
            IsValid(EstimationResource(caseNumber), traceParent, expires, signature);

        private static string ImageAnalysisResource(Guid caseNumber, int imageId) =>
            $"image-analysis:{caseNumber:D}:{imageId:G}";

        private static string EstimationResource(Guid caseNumber) =>
            $"estimation:{caseNumber:D}";

        private string BuildUrl(string path, string resource, string? traceParent)
        {
            var expires = _timeProvider.GetUtcNow().Add(_validity).ToUnixTimeSeconds();
            var query = new StringBuilder("?");

            if (!string.IsNullOrEmpty(traceParent))
            {
                query.Append(TraceParentParameter).Append('=').Append(Uri.EscapeDataString(traceParent)).Append('&');
            }

            query.Append(ExpiresParameter).Append('=').Append(expires.ToString(CultureInfo.InvariantCulture)).Append('&');
            query.Append(SignatureParameter).Append('=').Append(Sign(resource, traceParent, expires));

            return path + query;
        }

        // Neither the expiry nor a traceparent contains a newline, so the signed text is unambiguous.
        private string Sign(string resource, string? traceParent, long expires) =>
            Convert.ToHexStringLower(HMACSHA256.HashData(
                _key,
                Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{resource}\n{expires}\n{traceParent}"))));

        private bool IsValid(string resource, string? traceParent, long? expires, string? signature)
        {
            if (string.IsNullOrEmpty(signature) || expires is null)
            {
                return false;
            }

            // Check the signature first: an expiry nobody signed says nothing.
            var signed = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Sign(resource, traceParent, expires.Value)),
                Encoding.UTF8.GetBytes(signature.ToLowerInvariant()));

            return signed && _timeProvider.GetUtcNow().ToUnixTimeSeconds() <= expires.Value;
        }
    }
}

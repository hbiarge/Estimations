using System.Security.Claims;

namespace Acheve.Common.Shared
{
    /// <summary>The customer an access token was issued to.</summary>
    public static class CustomerClaims
    {
        /// <summary>Hard-coded per customer in the Keycloak realm.</summary>
        public const string ClaimType = "customer_id";

        /// <summary>The customer id in the token, or null when the claim is missing, empty or blank.</summary>
        public static string? CustomerIdOf(ClaimsPrincipal user) =>
            user.FindFirst(ClaimType)?.Value is { } value && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}

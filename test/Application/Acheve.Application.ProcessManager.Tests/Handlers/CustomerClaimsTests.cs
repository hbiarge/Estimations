using System.Security.Claims;
using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public sealed class CustomerClaimsTests
    {
        [Fact]
        public void Returns_the_customer_id_claim()
        {
            var user = UserWith(new Claim(CustomerClaims.ClaimType, "acme"));

            CustomerClaims.CustomerIdOf(user).ShouldBe("acme");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Returns_null_when_the_claim_is_missing_empty_or_whitespace(string? value)
        {
            var user = value is null
                ? UserWith()
                : UserWith(new Claim(CustomerClaims.ClaimType, value));

            CustomerClaims.CustomerIdOf(user).ShouldBeNull();
        }

        private static ClaimsPrincipal UserWith(params Claim[] claims) =>
            new(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}

using Acheve.Common.Shared;
using Microsoft.Extensions.Configuration;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class CustomerNotificationSignersTests
    {
        private static IConfiguration Configuration(params (string Customer, string Key)[] keys) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(keys.ToDictionary(
                    x => $"{Constants.CustomerCallbacks.SigningKeysSection}:{x.Customer}",
                    x => (string?)x.Key))
                .Build();

        [Fact]
        public void Reads_the_key_of_each_customer()
        {
            var signers = Configuration(("acme", "key-of-acme"), ("globex", "key-of-globex"))
                .GetNotificationSigners(requireKeys: true);

            signers.Count.ShouldBe(2);
            signers.For("acme").ShouldNotBeNull();
            signers.For("globex").ShouldNotBeNull();
            signers.For("initech").ShouldBeNull();
            signers.For(null).ShouldBeNull();
        }

        [Fact]
        public void Each_customer_has_their_own_signature()
        {
            var signers = Configuration(("acme", "key-of-acme"), ("globex", "key-of-globex"))
                .GetNotificationSigners(requireKeys: true);

            var (timestamp, signature) = signers.For("acme")!.Sign("body");

            signers.For("acme")!.IsValid("body", timestamp, signature).ShouldBeTrue();
            signers.For("globex")!.IsValid("body", timestamp, signature).ShouldBeFalse();
        }

        [Fact]
        public void Fails_at_startup_without_any_key_when_keys_are_required()
        {
            Should.Throw<InvalidOperationException>(
                () => Configuration().GetNotificationSigners(requireKeys: true));
        }

        [Fact]
        public void Allows_having_no_key_in_development()
        {
            var signers = Configuration().GetNotificationSigners(requireKeys: false);

            signers.Count.ShouldBe(0);
            signers.AllowUnsigned.ShouldBeTrue();
        }

        [Fact]
        public void A_blank_key_is_no_key()
        {
            var signers = Configuration(("acme", " ")).GetNotificationSigners(requireKeys: false);

            signers.For("acme").ShouldBeNull();
        }

        [Fact]
        public void A_fake_customer_endpoint_checks_with_the_key_of_the_customer_it_plays()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{Constants.CustomerCallbacks.SigningKeysSection}:acme"] = "key-of-acme",
                    [$"{Constants.CustomerCallbacks.SigningKeysSection}:globex"] = "key-of-globex",
                    [Constants.CustomerCallbacks.CustomerKey] = "globex"
                })
                .Build();

            var signer = configuration.GetNotificationSigner();
            var (timestamp, signature) = new NotificationSigner("key-of-globex").Sign("body");

            signer.ShouldNotBeNull();
            signer.IsValid("body", timestamp, signature).ShouldBeTrue();
        }
    }
}

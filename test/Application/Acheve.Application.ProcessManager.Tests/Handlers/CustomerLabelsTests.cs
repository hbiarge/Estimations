using Acheve.Common.Shared;
using Microsoft.Extensions.Configuration;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public sealed class CustomerLabelsTests
    {
        private readonly CustomerLabels _sut = new(["acme", "globex"]);

        [Fact]
        public void Known_customer_keeps_its_name() =>
            _sut.For("acme").ShouldBe("acme");

        [Theory]
        [InlineData("zzz")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("ACME")]
        public void Unknown_null_empty_and_differently_cased_ids_become_other(string? customerId) =>
            _sut.For(customerId).ShouldBe(CustomerLabels.Other);

        [Fact]
        public void All_is_known_plus_other() =>
            _sut.All.ShouldBe(["acme", "globex", CustomerLabels.Other], ignoreOrder: true);

        [Fact]
        public void A_thousand_distinct_ids_map_to_at_most_known_plus_one_values()
        {
            var labels = Enumerable.Range(0, 1000).Select(i => _sut.For($"customer-{i}")).Distinct();

            labels.Count().ShouldBeLessThanOrEqualTo(_sut.Known.Count + 1);
        }

        [Fact]
        public void FromConfiguration_reads_Customers_Known()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Customers:Known:0"] = "acme",
                    ["Customers:Known:1"] = "initech"
                })
                .Build();

            CustomerLabels.FromConfiguration(configuration).Known.ShouldBe(["acme", "initech"], ignoreOrder: true);
        }
    }
}

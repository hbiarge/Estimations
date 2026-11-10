using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Acheve.External.Shared.Tests
{
    public class BackgroundQueueOptionsTests
    {
        private static BackgroundQueueOptions Resolve(Dictionary<string, string?> settings)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            using var provider = new ServiceCollection()
                .AddLogging()
                .AddBackgroundQueue(configuration)
                .BuildServiceProvider();

            return provider.GetRequiredService<BackgroundQueueOptions>();
        }

        [Fact]
        public void Runs_twenty_items_at_a_time_by_default()
        {
            var options = Resolve([]);

            options.MaxConcurrency.ShouldBe(20);
            options.Capacity.ShouldBe(100);
        }

        [Fact]
        public void Reads_the_settings_from_the_BackgroundQueue_section()
        {
            var options = Resolve(new()
            {
                ["BackgroundQueue:MaxConcurrency"] = "5",
                ["BackgroundQueue:Capacity"] = "50"
            });

            options.MaxConcurrency.ShouldBe(5);
            options.Capacity.ShouldBe(50);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("many")]
        public void Ignores_values_that_are_not_positive_numbers(string value)
        {
            var options = Resolve(new() { ["BackgroundQueue:MaxConcurrency"] = value });

            options.MaxConcurrency.ShouldBe(20);
        }
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Acheve.External.Shared
{
    /// <summary>Settings of the background queue, read from the <c>BackgroundQueue</c> configuration section.</summary>
    public sealed class BackgroundQueueOptions
    {
        public const string SectionName = "BackgroundQueue";

        /// <summary>How many accepted jobs can wait before new requests block.</summary>
        public int Capacity { get; init; } = 100;

        /// <summary>How many jobs run at the same time.</summary>
        public int MaxConcurrency { get; init; } = 20;
    }

    public static class BackgroundQueueServiceCollectionExtensions
    {
        /// <summary>Adds the background queue and the hosted service that runs its work items.</summary>
        public static IServiceCollection AddBackgroundQueue(this IServiceCollection services, IConfiguration configuration)
        {
            var section = configuration.GetSection(BackgroundQueueOptions.SectionName);
            var defaults = new BackgroundQueueOptions();
            var options = new BackgroundQueueOptions
            {
                Capacity = ReadPositive(section, nameof(BackgroundQueueOptions.Capacity), defaults.Capacity),
                MaxConcurrency = ReadPositive(section, nameof(BackgroundQueueOptions.MaxConcurrency), defaults.MaxConcurrency)
            };

            services.AddSingleton(options);
            services.AddSingleton<IBackgroundTaskQueue>(new DefaultBackgroundTaskQueue(options.Capacity));
            services.AddHostedService<QueuedHostedService>();

            return services;
        }

        private static int ReadPositive(IConfigurationSection section, string key, int defaultValue) =>
            int.TryParse(section[key], out var value) && value > 0 ? value : defaultValue;
    }
}

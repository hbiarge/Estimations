using Acheve.Application.ProcessManager.Handlers;
using Acheve.Application.ProcessManager.Tests.Factories;
using Acheve.Common.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Hosting;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public sealed class EstimationMetricsStartupTests
    {
        [Fact]
        public async Task Metrics_are_created_when_the_host_starts_so_every_series_starts_at_zero_before_any_message()
        {
            var meterFactory = Sagas.CreateMeterFactory();
            var services = new ServiceCollection();
            services.AddSingleton(meterFactory);
            services.AddSingleton(Sagas.Customers);
            services.AddSingleton<EstimationMetrics>();
            services.AddHostedService<EstimationMetricsInitializer>();
            using var provider = services.BuildServiceProvider();
            using var started = new MetricCollector<long>(meterFactory, Constants.Telemetry.MeterName, "estimations.cases.started");

            foreach (var hosted in provider.GetServices<IHostedService>())
            {
                await hosted.StartAsync(TestContext.Current.CancellationToken);
            }

            started.GetMeasurementSnapshot().Select(x => x.Tags["estimation.customer_id"])
                .ShouldBe(["acme", "globex", CustomerLabels.Other], ignoreOrder: true);
        }
    }
}

using System.Diagnostics.Metrics;
using Acheve.Application.ProcessManager.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebus.Bus;

namespace Acheve.Application.ProcessManager.Tests.Factories
{
    public static class Sagas
    {
        public static EstimationSaga Create(IBus bus, EstimationMetrics? metrics = null, TimeProvider? timeProvider = null) =>
            new(
                bus,
                metrics ?? new EstimationMetrics(CreateMeterFactory()),
                timeProvider ?? TimeProvider.System,
                new NullLogger<EstimationSaga>());

        public static IMeterFactory CreateMeterFactory() =>
            new ServiceCollection()
                .AddMetrics()
                .BuildServiceProvider()
                .GetRequiredService<IMeterFactory>();
    }
}

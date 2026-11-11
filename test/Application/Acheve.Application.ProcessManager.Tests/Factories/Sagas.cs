using System.Diagnostics.Metrics;
using Acheve.Application.ProcessManager.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rebus.Bus;

namespace Acheve.Application.ProcessManager.Tests.Factories
{
    public static class Sagas
    {
        public static EstimationSaga Create(
            IBus bus,
            EstimationMetrics? metrics = null,
            TimeProvider? timeProvider = null,
            ICaseAttachments? attachments = null) =>
            new(
                bus,
                attachments ?? new FakeCaseAttachments(),
                metrics ?? new EstimationMetrics(CreateMeterFactory()),
                timeProvider ?? TimeProvider.System,
                new NullLogger<EstimationSaga>());

        public static IMeterFactory CreateMeterFactory() =>
            new ServiceCollection()
                .AddMetrics()
                .BuildServiceProvider()
                .GetRequiredService<IMeterFactory>();

        // Remembers the attachments the saga deleted.
        public sealed class FakeCaseAttachments : ICaseAttachments
        {
            public List<string> Deleted { get; } = [];

            public Task Delete(string ticket)
            {
                Deleted.Add(ticket);
                return Task.CompletedTask;
            }
        }
    }
}

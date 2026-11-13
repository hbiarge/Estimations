using Acheve.Common.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Rebus.Bus.Advanced;
using System.Diagnostics.Metrics;

namespace Acheve.Common.Consumption.Tests
{
    public class ConsumptionStateTests
    {
        private sealed class FakeWorkers(int count) : IWorkersApi
        {
            public int Count { get; private set; } = count;

            public List<int> Calls { get; } = [];

            public void SetNumberOfWorkers(int numberOfWorkers)
            {
                Calls.Add(numberOfWorkers);
                Count = numberOfWorkers;
            }
        }

        private sealed class Environment : IHostEnvironment
        {
            public string EnvironmentName { get; set; } = Environments.Development;
            public string ApplicationName { get; set; } = "imagedownloads";
            public string ContentRootPath { get; set; } = "";
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }

        private static ConsumptionState CreateState(FakeWorkers workers, out IMeterFactory meterFactory)
        {
            meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
            return new ConsumptionState(() => workers, meterFactory, new Environment(), NullLogger<ConsumptionState>.Instance);
        }

        [Fact]
        public async Task Pause_sets_the_workers_to_zero_and_resume_restores_the_count()
        {
            var workers = new FakeWorkers(3);
            var state = CreateState(workers, out _);

            await state.PauseAsync();

            workers.Calls.ShouldBe([0]);
            state.IsPaused.ShouldBeTrue();

            await state.ResumeAsync();

            workers.Calls.ShouldBe([0, 3]);
            state.IsPaused.ShouldBeFalse();
        }

        [Fact]
        public async Task Pausing_twice_keeps_the_original_count()
        {
            var workers = new FakeWorkers(2);
            var state = CreateState(workers, out _);

            await state.PauseAsync();
            await state.PauseAsync();
            await state.ResumeAsync();

            workers.Calls.ShouldBe([0, 2]);
        }

        [Fact]
        public async Task Resume_while_running_does_nothing()
        {
            var workers = new FakeWorkers(1);
            var state = CreateState(workers, out _);

            await state.ResumeAsync();

            workers.Calls.ShouldBeEmpty();
            state.IsPaused.ShouldBeFalse();
        }

        [Fact]
        public async Task Reports_paused()
        {
            var state = CreateState(new FakeWorkers(1), out var meterFactory);
            using var paused = new MetricCollector<int>(meterFactory, Constants.Telemetry.ConsumptionMeterName, "consumption.paused");

            await state.PauseAsync();
            paused.RecordObservableInstruments();

            var measurement = paused.GetMeasurementSnapshot().Single();
            measurement.Value.ShouldBe(1);
            measurement.Tags["messaging.consumer"].ShouldBe("imagedownloads");
        }
    }
}

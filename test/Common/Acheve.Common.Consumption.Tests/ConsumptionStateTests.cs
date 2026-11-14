using Acheve.Common.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
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

        // SetNumberOfWorkers(0) blocks until Release, like Rebus waiting for a slow handler to finish.
        private sealed class SlowWorkers(int count) : IWorkersApi
        {
            private readonly ManualResetEventSlim _release = new();

            public int Count { get; private set; } = count;

            public List<int> Calls { get; } = [];

            public ManualResetEventSlim Stopping { get; } = new();

            public void Release() => _release.Set();

            public void SetNumberOfWorkers(int numberOfWorkers)
            {
                lock (Calls)
                {
                    Calls.Add(numberOfWorkers);
                }

                if (numberOfWorkers == 0)
                {
                    Stopping.Set();
                    _release.Wait();
                }

                Count = numberOfWorkers;
            }
        }

        private static ConsumptionState CreateState(FakeWorkers workers, out IMeterFactory meterFactory)
        {
            meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
            return new ConsumptionState(() => workers, meterFactory, NullLogger<ConsumptionState>.Instance);
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
        public async Task A_resume_sent_during_a_slow_pause_still_resumes_when_its_caller_gave_up()
        {
            var workers = new SlowWorkers(1);
            var meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>();
            var state = new ConsumptionState(() => workers, meterFactory, NullLogger<ConsumptionState>.Instance);

            var pause = state.PauseAsync();
            workers.Stopping.Wait(TestContext.Current.CancellationToken);

            // The caller of the resume gives up (as the chaos monkey's HttpClient does after 10 s); the server keeps going.
            var resume = state.ResumeAsync();
            (await Task.WhenAny(resume, Task.Delay(100, TestContext.Current.CancellationToken))).ShouldNotBe(resume);

            workers.Release();
            await Task.WhenAll(pause, resume);

            workers.Calls.ShouldBe([0, 1]);
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
            // The service is already in service.name (service_name in Prometheus); no tag that repeats it differently.
            measurement.Tags.ShouldBeEmpty();
        }
    }
}

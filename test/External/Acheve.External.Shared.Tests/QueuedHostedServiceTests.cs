using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Acheve.External.Shared.Tests
{
    public class QueuedHostedServiceTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly DefaultBackgroundTaskQueue _queue = new(100);
        private readonly FakeLogger<QueuedHostedService> _logger = new();

        private QueuedHostedService CreateService(int maxConcurrency) =>
            new(_queue, new BackgroundQueueOptions { MaxConcurrency = maxConcurrency }, _logger);

        [Fact]
        public async Task Runs_items_in_parallel_up_to_the_limit_and_never_above_it()
        {
            var tracker = new ConcurrencyTracker();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var completed = new CountdownEvent(5);
            using var service = CreateService(maxConcurrency: 3);
            await service.StartAsync(TestContext.Current.CancellationToken);

            for (var i = 0; i < 5; i++)
            {
                await _queue.QueueBackgroundWorkItemAsync(async _ =>
                {
                    using (tracker.Enter())
                    {
                        await gate.Task;
                    }

                    completed.Signal();
                });
            }

            await tracker.WaitUntilRunningAsync(3);
            // Give the service the chance to start a fourth item if it would.
            await Task.Delay(200, TestContext.Current.CancellationToken);
            tracker.Running.ShouldBe(3);

            gate.SetResult();

            completed.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
            tracker.MaxRunning.ShouldBe(3);
            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task An_item_does_not_wait_for_a_slow_one_when_there_is_room()
        {
            var slowGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fastDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var service = CreateService(maxConcurrency: 2);
            await service.StartAsync(TestContext.Current.CancellationToken);

            await _queue.QueueBackgroundWorkItemAsync(async _ => await slowGate.Task);
            await _queue.QueueBackgroundWorkItemAsync(_ =>
            {
                fastDone.SetResult();
                return ValueTask.CompletedTask;
            });

            await fastDone.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            slowGate.SetResult();
            await service.StopAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task An_error_in_one_item_does_not_stop_the_others()
        {
            var completed = new CountdownEvent(3);
            using var service = CreateService(maxConcurrency: 2);
            await service.StartAsync(TestContext.Current.CancellationToken);

            await _queue.QueueBackgroundWorkItemAsync(_ => throw new InvalidOperationException("Boom"));
            await _queue.QueueBackgroundWorkItemAsync(async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("Boom later");
            });
            for (var i = 0; i < 3; i++)
            {
                await _queue.QueueBackgroundWorkItemAsync(_ =>
                {
                    completed.Signal();
                    return ValueTask.CompletedTask;
                });
            }

            completed.Wait(Timeout, TestContext.Current.CancellationToken).ShouldBeTrue();
            // Stopping waits for the item that throws later.
            await service.StopAsync(TestContext.Current.CancellationToken);
            _logger.Collector.GetSnapshot()
                .Count(r => r.Level == LogLevel.Error && r.Exception is InvalidOperationException)
                .ShouldBe(2);
        }

        [Fact]
        public async Task Stopping_waits_for_the_running_items_without_throwing()
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finished = false;
            using var service = CreateService(maxConcurrency: 2);
            await service.StartAsync(TestContext.Current.CancellationToken);

            await _queue.QueueBackgroundWorkItemAsync(async _ =>
            {
                started.SetResult();
                await gate.Task;
                finished = true;
            });
            await started.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            var stopping = service.StopAsync(TestContext.Current.CancellationToken);
            await Task.Delay(200, TestContext.Current.CancellationToken);
            stopping.IsCompleted.ShouldBeFalse();

            gate.SetResult();
            await stopping.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            finished.ShouldBeTrue();
            _logger.Collector.GetSnapshot().ShouldNotContain(r => r.Level == LogLevel.Error);
        }

        [Fact]
        public async Task Stopping_does_not_throw_when_an_item_honours_the_cancellation()
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var service = CreateService(maxConcurrency: 2);
            await service.StartAsync(TestContext.Current.CancellationToken);

            await _queue.QueueBackgroundWorkItemAsync(async cancellationToken =>
            {
                started.SetResult();
                await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken);
            });
            await started.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);

            await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(Timeout, TestContext.Current.CancellationToken);

            _logger.Collector.GetSnapshot().ShouldNotContain(r => r.Level == LogLevel.Error);
        }

        private sealed class ConcurrencyTracker
        {
            private int _running;
            private int _maxRunning;

            public int Running => Volatile.Read(ref _running);

            public int MaxRunning => Volatile.Read(ref _maxRunning);

            public IDisposable Enter()
            {
                var running = Interlocked.Increment(ref _running);
                int max;
                do
                {
                    max = Volatile.Read(ref _maxRunning);
                }
                while (running > max && Interlocked.CompareExchange(ref _maxRunning, running, max) != max);

                return new Exit(this);
            }

            public async Task WaitUntilRunningAsync(int expected)
            {
                var deadline = DateTime.UtcNow + Timeout;
                while (Running < expected)
                {
                    DateTime.UtcNow.ShouldBeLessThan(deadline, $"Only {Running} items started.");
                    await Task.Delay(10);
                }
            }

            private sealed class Exit(ConcurrencyTracker tracker) : IDisposable
            {
                public void Dispose() => Interlocked.Decrement(ref tracker._running);
            }
        }
    }
}

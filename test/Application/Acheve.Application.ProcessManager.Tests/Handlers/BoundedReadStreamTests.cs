using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class BoundedReadStreamTests
    {
        [Fact]
        public async Task Copies_a_stream_within_the_limit()
        {
            await using var source = new BoundedReadStream(new MemoryStream(new byte[100]), 100, CancellationToken.None);
            using var target = new MemoryStream();

            await source.CopyToAsync(target, TestContext.Current.CancellationToken);

            target.Length.ShouldBe(100);
        }

        [Fact]
        public async Task Stops_a_stream_larger_than_the_limit()
        {
            await using var source = new BoundedReadStream(new MemoryStream(new byte[101]), 100, CancellationToken.None);
            using var target = new MemoryStream();

            await Should.ThrowAsync<InvalidOperationException>(
                () => source.CopyToAsync(target, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Stops_reading_when_the_deadline_passes()
        {
            using var deadline = new CancellationTokenSource();
            await using var source = new BoundedReadStream(new MemoryStream(new byte[10]), 100, deadline.Token);
            await deadline.CancelAsync();

            await Should.ThrowAsync<OperationCanceledException>(
                async () => await source.ReadExactlyAsync(new byte[10], TestContext.Current.CancellationToken));
        }
    }
}

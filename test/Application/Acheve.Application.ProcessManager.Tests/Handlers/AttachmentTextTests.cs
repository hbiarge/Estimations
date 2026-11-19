using System.Text;
using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public class AttachmentTextTests
    {
        [Fact]
        public async Task Reads_a_text_within_the_limit()
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"metadata\":\"ñ\"}"));

            var text = await AttachmentText.ReadAsync(stream, 100, TestContext.Current.CancellationToken);

            text.ShouldBe("{\"metadata\":\"ñ\"}");
        }

        [Fact]
        public async Task Refuses_a_text_over_the_limit()
        {
            var stream = new MemoryStream(new byte[101]);

            await Should.ThrowAsync<InvalidOperationException>(
                () => AttachmentText.ReadAsync(stream, 100, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Disposes_the_stream()
        {
            var stream = new MemoryStream(new byte[10]);

            await AttachmentText.ReadAsync(stream, 100, TestContext.Current.CancellationToken);

            stream.CanRead.ShouldBeFalse();
        }
    }
}

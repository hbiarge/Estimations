namespace Acheve.Common.Shared
{
    /// <summary>
    /// Reads another stream up to a number of bytes and until a cancellation token fires.
    /// Protects a copy of a response body from a server that sends too much or too slowly.
    /// </summary>
    public sealed class BoundedReadStream(Stream inner, long maxBytes, CancellationToken deadline) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline, cancellationToken);

            return Count(await inner.ReadAsync(buffer, linked.Token));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
        {
            deadline.ThrowIfCancellationRequested();

            return Count(inner.Read(buffer, offset, count));
        }

        private int Count(int bytes)
        {
            _read += bytes;

            if (_read > maxBytes)
            {
                throw new InvalidOperationException($"The content is larger than the limit of {maxBytes} bytes.");
            }

            return bytes;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

namespace Acheve.Common.Shared
{
    /// <summary>
    /// Reads a text attachment of the data bus (an estimation, the analysis of an image) into memory, up to a limit.
    /// The Api already limits the callbacks that create them; this also protects the readers from an attachment
    /// that got there another way.
    /// </summary>
    public static class AttachmentText
    {
        public const long DefaultMaxBytes = 2 * 1024 * 1024;

        /// <summary>Reads the whole stream, and disposes it. Throws InvalidOperationException above the limit.</summary>
        public static async Task<string> ReadAsync(
            Stream stream, long maxBytes = DefaultMaxBytes, CancellationToken cancellationToken = default)
        {
            await using var bounded = new BoundedReadStream(stream, maxBytes, cancellationToken);
            using var reader = new StreamReader(bounded);

            return await reader.ReadToEndAsync(cancellationToken);
        }
    }
}

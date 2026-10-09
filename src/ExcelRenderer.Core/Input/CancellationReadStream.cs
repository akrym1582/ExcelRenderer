namespace ExcelRenderer.Core.Input;

/// <summary>Checks cancellation at XML input buffer boundaries without owning the source.</summary>
internal sealed class CancellationReadStream(Stream source, CancellationToken cancellationToken) : Stream
{
    /// <inheritdoc/>
    public override bool CanRead => source.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => source.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => source.Length;

    /// <inheritdoc/>
    public override long Position { get => source.Position; set => source.Position = value; }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return source.Read(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

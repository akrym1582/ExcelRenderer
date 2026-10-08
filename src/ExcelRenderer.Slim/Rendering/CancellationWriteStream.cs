namespace ExcelRenderer.Slim.Rendering;

/// <summary>Checks cancellation at PDF writes without owning or buffering the destination.</summary>
internal sealed class CancellationWriteStream(Stream source, CancellationToken cancellationToken) : Stream
{
    /// <inheritdoc/>
    public override bool CanRead => source.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => source.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => source.CanWrite;

    /// <inheritdoc/>
    public override long Length => source.Length;

    /// <inheritdoc/>
    public override long Position { get => source.Position; set => source.Position = value; }

    /// <inheritdoc/>
    public override void Flush() => source.Flush();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);

    /// <inheritdoc/>
    public override void SetLength(long value) => source.SetLength(value);

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        cancellationToken.ThrowIfCancellationRequested();
        source.Write(buffer, offset, count);
    }
}

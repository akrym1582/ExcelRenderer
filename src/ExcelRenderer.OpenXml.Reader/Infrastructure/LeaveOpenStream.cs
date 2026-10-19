using System.IO;

namespace ExcelRenderer.OpenXml.Reader.Infrastructure;

/// <summary>Provides Stream metadata.</summary>
internal sealed class LeaveOpenStream : Stream
{
    private readonly Stream source;

    /// <summary>Initializes a new instance of the <see cref="LeaveOpenStream"/> class.</summary>
    /// <param name="source">The source value.</param>
    internal LeaveOpenStream(Stream source) => this.source = source;

    /// <inheritdoc/>
    public override bool CanRead => source.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => source.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => source.Length;

    /// <inheritdoc/>
    public override long Position
    {
        get => source.Position;
        set => source.Position = value;
    }

    /// <inheritdoc/>
    public override void Flush() => source.Flush();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new System.NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new System.NotSupportedException();
}

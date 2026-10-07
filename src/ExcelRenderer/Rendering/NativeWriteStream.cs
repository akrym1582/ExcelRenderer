using System.Runtime.ExceptionServices;

namespace ExcelRenderer.Rendering;

/// <summary>Defers stream failures until execution has returned from a native Skia write callback.</summary>
internal sealed class NativeWriteStream : Stream
{
    private readonly Stream output;
    private ExceptionDispatchInfo? failure;
    private long written;

    /// <summary>Initializes a new instance of the <see cref="NativeWriteStream"/> class.</summary>
    /// <param name="output">The caller-owned intermediate buffer.</param>
    internal NativeWriteStream(Stream output) => this.output = output;

    /// <inheritdoc/>
    public override bool CanRead => false;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => written;

    /// <inheritdoc/>
    public override long Position { get => written; set => throw new NotSupportedException(); }

    /// <inheritdoc/>
    public override void Flush()
    {
        try
        {
            if (failure is null)
            {
                output.Flush();
            }
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
        }
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        try
        {
            if (failure is null)
            {
                output.Write(buffer, offset, count);
                written += count;
            }
        }
        catch (Exception error)
        {
            // Managed exceptions must never unwind a native write callback (which can terminate the process).
            failure = ExceptionDispatchInfo.Capture(error);
        }
    }

    /// <summary>Rethrows the original failure after returning to managed drawing code.</summary>
    internal void ThrowIfFailed() => failure?.Throw();
}

namespace ExcelRenderer.Slim.Input;

/// <summary>An independent read cursor over an owner-held seekable buffer.</summary>
internal sealed class BufferReadStream : Stream
{
    private readonly Stream source;
    private long position;

    /// <summary>Initializes a new instance of the <see cref="BufferReadStream"/> class.</summary>
    /// <param name="source">The source used by this operation.</param>
    internal BufferReadStream(Stream source) => this.source = source;

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
        get => position;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            position = value;
        }
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        lock (source)
        {
            var saved = source.Position;
            try
            {
                source.Position = position;
                var read = source.Read(buffer, offset, count);
                position += read;
                return read;
            }
            finally
            {
                source.Position = saved;
            }
        }
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(position + offset),
            SeekOrigin.End => checked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return position;
    }

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

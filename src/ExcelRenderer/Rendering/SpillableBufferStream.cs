namespace ExcelRenderer.Rendering;

/// <summary>Owns a bounded memory buffer that migrates once to an exclusive temporary file.</summary>
internal sealed class SpillableBufferStream : Stream
{
    private readonly RenderBufferOptions options;
    private Stream inner = new MemoryStream();
    private string? temporaryPath;
    private bool disposed;
    private int peakMemoryCapacity;

    /// <summary>Initializes a new instance of the <see cref="SpillableBufferStream"/> class.</summary>
    /// <param name="options">The options used by this operation.</param>
    internal SpillableBufferStream(RenderBufferOptions options)
    {
        options.Validate();
        this.options = options;
    }

    /// <inheritdoc/>
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc/>
    public override bool CanSeek => inner.CanSeek;

    /// <inheritdoc/>
    public override bool CanWrite => inner.CanWrite;

    /// <inheritdoc/>
    public override long Length => inner.Length;

    /// <inheritdoc/>
    public override long Position { get => inner.Position; set => inner.Position = value; }

    /// <summary>Gets a value indicating whether the buffer has migrated to a temporary file.</summary>
    internal bool HasSpilled => temporaryPath is not null;

    /// <summary>Gets the allocated in-memory capacity for structural buffer verification.</summary>
    internal int MemoryCapacity => inner is MemoryStream memory ? memory.Capacity : 0;

    /// <summary>Gets the peak allocated memory capacity before any spill.</summary>
    internal int PeakMemoryCapacity => peakMemoryCapacity;

    /// <inheritdoc/>
    public override void Flush() => inner.Flush();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        EnsureCapacity(value);
        inner.SetLength(value);
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (offset < 0 || count < 0 || offset > buffer.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        EnsureCapacity(checked(Position + count));
        inner.Write(buffer, offset, count);
    }

    /// <inheritdoc/>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureCapacity(checked(Position + buffer.Length));
        inner.Write(buffer);
    }

    /// <inheritdoc/>
    public override void WriteByte(byte value)
    {
        EnsureCapacity(checked(Position + 1));
        inner.WriteByte(value);
    }

    /// <inheritdoc/>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(buffer, offset, count);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Opens an independent read cursor over the owned workbook spool.</summary>
    /// <returns>The planned or generated result.</returns>
    internal Stream OpenRead()
    {
        inner.Flush();
        if (inner is MemoryStream memory && memory.TryGetBuffer(out var segment))
        {
            // Prepared input is immutable: each reader can share bytes without copying or seek/restore locking.
            return new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false, publiclyVisible: true);
        }

        return new BufferReadStream(inner);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            try
            {
                if (inner is MemoryStream memory)
                {
                    // An async caller may retain this disposed owner; release its byte array immediately.
                    memory.SetLength(0);
                    memory.Capacity = 0;
                }

                inner.Dispose();
            }
            finally
            {
                if (temporaryPath is not null)
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        // Cleanup must not hide a drawing, cancellation, or output exception.
                    }
                }
            }
        }

        base.Dispose(disposing);
    }

    private void EnsureCapacity(long required)
    {
        if (required < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(required));
        }

        if (inner is not MemoryStream memory)
        {
            return;
        }

        if (required > options.MemoryThresholdBytes || required > int.MaxValue)
        {
            if (!options.AllowTemporaryFiles)
            {
                throw new InvalidDataException("Buffer exceeds MemoryThresholdBytes and temporary files are disabled.");
            }

            var path = Path.Combine(options.TemporaryDirectory ?? Path.GetTempPath(), "excelrenderer-" + Guid.NewGuid().ToString("N") + ".tmp");
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            temporaryPath = path;
            try
            {
                var position = memory.Position;
                memory.Position = 0;
                memory.CopyTo(file);
                file.Position = position;
                inner = file;
                memory.Dispose();
                ConversionMetrics.Report("buffer.spillCount", 1);
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }
        else if (required > memory.Capacity)
        {
            // Set capacity before writing so MemoryStream cannot double past the configured limit.
            memory.Capacity = (int)Math.Min(int.MaxValue, Math.Min(options.MemoryThresholdBytes, Math.Max(required, Math.Max(256L, (long)memory.Capacity * 2))));
            peakMemoryCapacity = Math.Max(peakMemoryCapacity, memory.Capacity);
        }
    }
}

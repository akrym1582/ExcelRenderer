namespace ExcelRenderer.Core.Tests;

// Also compiled by the main tests. Core's standalone suite needs no shared fixture directory.
internal sealed class AsyncOnlyWriteStream : Stream
{
    private readonly MemoryStream received = new();
    private int active;
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource? Gate { get; init; }
    internal int FailWrite { get; init; }
    internal bool FailFlush { get; init; }
    internal bool Disposed { get; private set; }
    internal int Writes { get; private set; }
    internal int Flushes { get; private set; }
    internal int MaxActive { get; private set; }
    internal int MaxWriteBytes { get; private set; }
    internal byte[] Bytes => received.ToArray();
    public override bool CanWrite => true;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override long Length => throw new InvalidOperationException("Length");
    public override long Position { get => throw new InvalidOperationException("Position"); set => throw new InvalidOperationException("Position"); }
    public override void Flush() => throw new InvalidOperationException("Flush");
    public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Write");
    public override void WriteByte(byte value) => throw new InvalidOperationException("WriteByte");
    public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Read");
    public override long Seek(long offset, SeekOrigin origin) => throw new InvalidOperationException("Seek");
    public override void SetLength(long value) => throw new InvalidOperationException("SetLength");
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Writes++;
        MaxActive = Math.Max(MaxActive, ++active);
        MaxWriteBytes = Math.Max(MaxWriteBytes, buffer.Length);
        Entered.TrySetResult();
        try
        {
            if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
            else await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (Writes == FailWrite) throw new IOException("write failure");
            await received.WriteAsync(buffer, cancellationToken);
        }
        finally { active--; }
    }
    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Flushes++;
        return FailFlush ? Task.FromException(new IOException("flush failure")) : Task.CompletedTask;
    }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

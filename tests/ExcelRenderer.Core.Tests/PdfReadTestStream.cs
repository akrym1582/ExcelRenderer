namespace ExcelRenderer.Core.Tests;

/// <summary>Observes real input operations and can force short reads or truncate the physical input.</summary>
internal sealed class PdfReadTestStream(byte[] bytes, int maxRead = int.MaxValue, long? reportedLength = null) : Stream
{
    private readonly MemoryStream source = new(bytes, writable: false);

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length { get { LengthGets++; return reportedLength ?? source.Length; } }
    public override long Position { get { PositionGets++; return source.Position; } set { PositionSets++; source.Position = value; } }
    internal int Reads { get; private set; }
    internal int ReadBytes { get; private set; }
    internal int ReadByteCalls { get; private set; }
    internal int PositionSets { get; private set; }
    internal int PositionGets { get; private set; }
    internal int LengthGets { get; private set; }
    internal int Seeks { get; private set; }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count)
    {
        Reads++;
        var read = source.Read(buffer, offset, Math.Min(count, maxRead));
        ReadBytes += read;
        return read;
    }
    public override int ReadByte() { ReadByteCalls++; return source.ReadByte(); }
    public override long Seek(long offset, SeekOrigin origin) { Seeks++; return source.Seek(offset, origin); }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) source.Dispose();
        base.Dispose(disposing);
    }
}

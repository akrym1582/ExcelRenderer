namespace ExcelRenderer.Core.Input;

/// <summary>Preserves the Core owner boundary while delegating storage to OpenXml.</summary>
internal sealed class PreparedWorkbook : IDisposable
{
    private readonly OpenXml.Reader.Input.PreparedWorkbook inner;

    /// <summary>Initializes a new instance of the <see cref="PreparedWorkbook"/> class.</summary>
    /// <param name="buffer">The owned shared spool.</param>
    internal PreparedWorkbook(SpillableBufferStream buffer) => inner = new(buffer);

    /// <summary>Initializes a new instance of the <see cref="PreparedWorkbook"/> class.</summary>
    /// <param name="inner">The validated shared owner.</param>
    internal PreparedWorkbook(OpenXml.Reader.Input.PreparedWorkbook inner) => this.inner = inner;

    /// <inheritdoc/>
    public void Dispose() => inner.Dispose();

    /// <summary>Opens an independent read cursor without copying bytes.</summary>
    /// <returns>A borrowed read stream.</returns>
    internal Stream OpenRead() => inner.OpenRead();
}

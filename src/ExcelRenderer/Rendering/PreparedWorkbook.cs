namespace ExcelRenderer.Rendering;

/// <summary>Preserves the main internal workbook-owner boundary while delegating storage to Core.</summary>
internal sealed class PreparedWorkbook : IDisposable
{
    private readonly Core.Input.PreparedWorkbook inner;

    /// <summary>Initializes a new instance of the <see cref="PreparedWorkbook"/> class.</summary>
    /// <param name="buffer">The shared spool.</param>
    internal PreparedWorkbook(SpillableBufferStream buffer) => inner = new(buffer);

    /// <summary>Initializes a new instance of the <see cref="PreparedWorkbook"/> class.</summary>
    /// <param name="inner">The validated shared owner.</param>
    internal PreparedWorkbook(Core.Input.PreparedWorkbook inner) => this.inner = inner;

    /// <summary>Gets the shared prepared workbook without copying storage.</summary>
    internal Core.Input.PreparedWorkbook CoreWorkbook => inner;

    /// <inheritdoc/>
    public void Dispose() => inner.Dispose();

    /// <summary>Opens an independent cursor without copying workbook bytes.</summary>
    /// <returns>The borrowed read stream.</returns>
    internal Stream OpenRead() => inner.OpenRead();
}

namespace ExcelRenderer.Core.Input;

/// <summary>Owns the validated spool and opens independent seekable readers without copying workbook bytes.</summary>
internal sealed class PreparedWorkbook : IDisposable
{
    private readonly SpillableBufferStream buffer;

    /// <summary>Initializes a new instance of the <see cref="PreparedWorkbook"/> class.</summary>
    /// <param name="buffer">The buffer used by this operation.</param>
    internal PreparedWorkbook(SpillableBufferStream buffer) => this.buffer = buffer;

    /// <inheritdoc/>
    public void Dispose() => buffer.Dispose();

    /// <summary>Opens an independent read cursor over the owned workbook spool.</summary>
    /// <returns>The planned or generated result.</returns>
    internal Stream OpenRead() => buffer.OpenRead();
}

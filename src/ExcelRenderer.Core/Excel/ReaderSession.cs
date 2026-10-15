using ClosedXML.Excel;
using ClosedXML.Graphics;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Rendering;

namespace ExcelRenderer.Core.Excel;

/// <summary>Owns bounded style conversion and scratch formatting resources for one read.</summary>
internal sealed class ReaderSession : IDisposable
{
    private const int MaximumStyles = 8192;
    private readonly bool use1904DateSystem;
    private readonly IXLGraphicEngine? graphics;
    private readonly Dictionary<(IXLStyle Style, XLDataType Type), CellStyle> styles = new();
    private XLWorkbook? scratch;
    private IXLCell? scratchCell;
    private int conversions;
    private int hits;

    /// <summary>Initializes a new instance of the <see cref="ReaderSession"/> class.</summary>
    /// <param name="use1904DateSystem">The source date system.</param>
    /// <param name="graphics">The source graphics engine.</param>
    internal ReaderSession(bool use1904DateSystem, IXLGraphicEngine? graphics)
    {
        this.use1904DateSystem = use1904DateSystem;
        this.graphics = graphics;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        scratch?.Dispose();
        ConversionMetrics.Report("readerScratchWorkbooks", scratch is null ? 0 : 1);
        ConversionMetrics.Report("readerStyleConversions", conversions);
        ConversionMetrics.Report("readerStyleCacheHits", hits);
        ConversionMetrics.Report("readerStyleCacheEntries", styles.Count);
    }

    /// <summary>Formats a value without evaluating the source formula.</summary>
    /// <param name="source">The source style.</param>
    /// <param name="value">The selected display value.</param>
    /// <returns>The formatted display.</returns>
    internal string Format(IXLCell source, XLCellValue value)
    {
        if (scratch is null)
        {
            scratch = graphics is null ? new XLWorkbook() : new XLWorkbook(new LoadOptions { GraphicEngine = graphics });
            scratch.Use1904DateSystem = use1904DateSystem;
            scratchCell = scratch.AddWorksheet("cached").Cell(1, 1);
        }

        scratchCell!.Clear(XLClearOptions.All);
        scratchCell.Style = source.Style;
        scratchCell.Value = value;
        return scratchCell.GetFormattedString();
    }

    /// <summary>Reuses value-equal source styles within one product reader.</summary>
    /// <param name="cell">The read-only source cell.</param>
    /// <param name="convert">The product style policy.</param>
    /// <returns>The converted immutable style.</returns>
    internal CellStyle ConvertStyle(IXLCell cell, Func<IXLCell, CellStyle> convert)
    {
        // The reader never changes source styles. ClosedXML style equality and hashing use
        // value keys; scratch assignments copy that value and never mutate the source wrapper.
        // Keeping this cache local also separates Core normalization from full product policy.
        var key = (cell.Style, cell.CachedValue.Type);
        if (styles.TryGetValue(key, out var style))
        {
            hits++;
            return style;
        }

        conversions++;
        style = convert(cell);
        if (styles.Count < MaximumStyles)
        {
            styles.Add(key, style);
        }

        return style;
    }
}

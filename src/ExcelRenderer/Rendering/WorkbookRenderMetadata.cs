using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.Rendering;

/// <summary>Retains workbook identity, names, hyperlinks, merges, and original geometry without sheet bodies.</summary>
internal sealed class WorkbookRenderMetadata
{
    private readonly IReadOnlyDictionary<string, SheetGeometry> geometry;

    /// <summary>Initializes a new instance of the <see cref="WorkbookRenderMetadata"/> class.</summary>
    /// <param name="document">The reader document whose heavy bodies must not remain attached.</param>
    internal WorkbookRenderMetadata(ReportDocument document)
    {
        Sheets = document.Sheets.Select(sheet => sheet with
        {
            Cells = new Dictionary<CellAddress, ReportCell>(),
            Images = [],
            Shapes = [],
        }).ToArray();
        geometry = Sheets.ToDictionary(sheet => sheet.Name, sheet => new SheetGeometry(sheet), StringComparer.Ordinal);
    }

    /// <summary>Gets lightweight sheets in original workbook order for named target resolution.</summary>
    internal IReadOnlyList<ReportSheet> Sheets { get; }

    /// <summary>Gets immutable original geometry for a source or target worksheet.</summary>
    /// <param name="sheetName">The original worksheet name.</param>
    /// <returns>The shared origin geometry.</returns>
    internal SheetGeometry Geometry(string sheetName) => geometry[sheetName];
}

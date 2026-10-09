using ExcelRenderer.Layout;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Maps public layout boundaries using immutable dictionary projections.</summary>
internal static partial class CoreModelAdapter
{
    /// <summary>Projects a public sheet once at the layout boundary.</summary>
    /// <param name="value">The caller or reader-projected public sheet.</param>
    /// <returns>The common model, borrowing original dictionaries where available.</returns>
    internal static Core.Model.ReportSheet ToCore(ReportSheet value)
    {
        var styles = new Dictionary<CellStyle, Core.Model.CellStyle>();
        var sourceCells = value.Cells is CoreDictionaryView<Core.Model.CellAddress, Core.Model.ReportCell, CellAddress, ReportCell> cells
            ? cells.Source
            : new CoreDictionaryView<CellAddress, ReportCell, Core.Model.CellAddress, Core.Model.ReportCell>(value.Cells, ToCore, ToPublic, Cell);
        return new(
            value.Name,
            sourceCells,
            value.Columns is CoreDictionaryView<int, Core.Model.ColumnDefinition, int, ColumnDefinition> columns ? columns.Source : new CoreDictionaryView<int, ColumnDefinition, int, Core.Model.ColumnDefinition>(value.Columns, key => key, key => key, column => new(column.Width, column.IsHidden)),
            value.Rows is CoreDictionaryView<int, Core.Model.RowDefinition, int, RowDefinition> rows ? rows.Source : new CoreDictionaryView<int, RowDefinition, int, Core.Model.RowDefinition>(value.Rows, key => key, key => key, row => new(row.Height, row.IsHidden)),
            value.MergedRanges.Select(ToCore).ToArray(),
            ToCore(value.PageSettings),
            value.PrintArea is { } area ? ToCore(area) : null,
            value.Images?.Select(ToCore).ToArray(),
            value.HeaderFooter is { } header ? ToCoreHeader(header) : null)
        {
            DefaultColumnWidth = value.DefaultColumnWidth,
            DefaultRowHeight = value.DefaultRowHeight,
            PrintAreas = value.PrintAreas.Select(ToCore).ToArray(),
            SourceSheetIndex = value.SourceSheetIndex,
            RequestedRange = value.RequestedRange is { } requested ? ToCore(requested) : null,
            RenderUsedRange = value.RenderUsedRange is { } used ? ToCore(used) : null,
            ExtensionData = new FullSheetData(value.Shapes ?? [], new Excel.SheetHyperlinkMetadata(value.Hyperlinks, value.HyperlinkNames, new Dictionary<CellAddress, string>())),
        };

        Core.Model.ReportCell Cell(ReportCell cell)
        {
            if (!styles.TryGetValue(cell.Style, out var style))
            {
                style = ToCore(cell.Style);
                styles[cell.Style] = style;
            }

            return new(cell.Text, style, cell.RowSpan, cell.ColumnSpan, cell.Formula)
            {
                MergedBorders = cell.MergedBorders?.Select(border => new Core.Model.CellBorder(ToCore(border.Address), ToCore(border.Border))).ToArray(),
            };
        }
    }

    /// <summary>Maps an original anchor without rounding.</summary>
    /// <param name="value">The original public anchor.</param>
    /// <returns>The neutral coordinates.</returns>
    internal static Core.Model.DrawingAnchor ToCore(DrawingAnchor value) => new(
        value.Kind switch
        {
            DrawingAnchorKind.Absolute => Core.Model.DrawingAnchorKind.Absolute,
            DrawingAnchorKind.OneCell => Core.Model.DrawingAnchorKind.OneCell,
            DrawingAnchorKind.TwoCell => Core.Model.DrawingAnchorKind.TwoCell,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        },
        value.From is { } from ? ToCore(from) : null,
        value.FromOffsetX,
        value.FromOffsetY,
        value.To is { } to ? ToCore(to) : null,
        value.ToOffsetX,
        value.ToOffsetY,
        value.PositionX,
        value.PositionY,
        value.ExtentWidth,
        value.ExtentHeight,
        value.EditAs);

    /// <summary>Maps a rectangle without changing units.</summary>
    /// <param name="value">The original rectangle.</param>
    /// <returns>The common point rectangle.</returns>
    internal static Core.Layout.ReportRect ToCore(ReportRect value) => new(value.X, value.Y, value.Width, value.Height);

    /// <summary>Maps a rectangle without changing units.</summary>
    /// <param name="value">The common rectangle.</param>
    /// <returns>The public point rectangle.</returns>
    internal static ReportRect ToPublic(Core.Layout.ReportRect value) => new(value.X, value.Y, value.Width, value.Height);

    /// <summary>Maps a page band without changing half-open bounds.</summary>
    /// <param name="value">The public band.</param>
    /// <returns>The common band.</returns>
    internal static Core.Layout.PageBand ToCore(PageBand value) => new(value.Start, value.End);

    /// <summary>Maps a page band without changing half-open bounds.</summary>
    /// <param name="value">The common band.</param>
    /// <returns>The public band.</returns>
    internal static PageBand ToPublic(Core.Layout.PageBand value) => new(value.Start, value.End);

    /// <summary>Materializes one page's durable public cell data.</summary>
    /// <param name="value">The common cell.</param>
    /// <param name="styles">The page-local style pool.</param>
    /// <returns>The public cell.</returns>
    internal static ReportCell ToPublic(Core.Model.ReportCell value, Excel.StylePool styles) => new(value.Text, styles.Intern(ToPublic(value.Style)), value.RowSpan, value.ColumnSpan, value.Formula)
    {
        MergedBorders = value.MergedBorders?.Select(border => new CellBorder(ToPublic(border.Address), styles.Intern(ToPublic(border.Border)))).ToArray(),
    };

    /// <summary>Maps header metadata without projecting sheet objects.</summary>
    /// <param name="value">The original header/footer.</param>
    /// <returns>The neutral fields.</returns>
    internal static Core.Model.HeaderFooter ToCoreHeader(HeaderFooter value) => new(
        ToCore(value.Header),
        ToCore(value.Footer),
        value.FirstPageHeader is { } firstHeader ? ToCore(firstHeader) : null,
        value.FirstPageFooter is { } firstFooter ? ToCore(firstFooter) : null,
        value.EvenPageHeader is { } evenHeader ? ToCore(evenHeader) : null,
        value.EvenPageFooter is { } evenFooter ? ToCore(evenFooter) : null);

    private static Core.Model.ReportImage ToCore(ReportImage value) => new(ToCore(value.Anchor), value.OffsetX, value.OffsetY, value.Width, value.Height, value.ImageBytes, value.ZIndex, value.Name, value.ContentType, value.Extension)
    {
        DrawingAnchor = value.DrawingAnchor is { } anchor ? ToCore(anchor) : null,
        ExtensionData = new FullImageData(value),
    };

    private static Core.Model.HeaderFooterSection ToCore(HeaderFooterSection value) => new(value.Left, value.Center, value.Right);
}

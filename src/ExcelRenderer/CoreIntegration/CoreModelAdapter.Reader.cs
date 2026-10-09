using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Projects reader models without allocating a second cell dictionary.</summary>
internal static partial class CoreModelAdapter
{
    /// <summary>Projects a common sheet at the public model boundary without duplicating all cells.</summary>
    /// <param name="value">The common sheet.</param>
    /// <param name="stylePool">The workbook-local public style pool.</param>
    /// <returns>The durable public model view.</returns>
    internal static ReportSheet ToPublic(Core.Model.ReportSheet value, Excel.StylePool stylePool)
    {
        var styles = new Dictionary<Core.Model.CellStyle, CellStyle>();
        var borders = new Dictionary<Core.Model.BorderStyle, BorderStyle>();
        var data = value.ExtensionData as FullSheetData;
        return new ReportSheet(
            value.Name,
            new CoreDictionaryView<Core.Model.CellAddress, Core.Model.ReportCell, CellAddress, ReportCell>(value.Cells, ToPublic, ToCore, ProjectCell),
            new CoreDictionaryView<int, Core.Model.ColumnDefinition, int, ColumnDefinition>(value.Columns, key => key, key => key, column => new(column.Width, column.IsHidden)),
            new CoreDictionaryView<int, Core.Model.RowDefinition, int, RowDefinition>(value.Rows, key => key, key => key, row => new(row.Height, row.IsHidden)),
            value.MergedRanges.Select(ToPublic).ToArray(),
            ToPublic(value.PageSettings),
            value.PrintArea is { } area ? ToPublic(area) : null,
            value.Images?.Select(ToPublicImage).ToArray(),
            value.HeaderFooter is { } headerFooter ? ToPublic(headerFooter) : null,
            data?.Shapes)
        {
            DefaultColumnWidth = value.DefaultColumnWidth,
            DefaultRowHeight = value.DefaultRowHeight,
            PrintAreas = value.PrintAreas.Select(ToPublic).ToArray(),
            SourceSheetIndex = value.SourceSheetIndex,
            Hyperlinks = data?.Hyperlinks?.Links ?? [],
            HyperlinkNames = data?.Hyperlinks?.Names ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        ReportCell ProjectCell(Core.Model.ReportCell cell)
        {
            if (!styles.TryGetValue(cell.Style, out var style))
            {
                style = stylePool.Intern(ToPublic(cell.Style));
                styles[cell.Style] = style;
            }

            return new(cell.Text, style, cell.RowSpan, cell.ColumnSpan, cell.Formula)
            {
                MergedBorders = cell.MergedBorders?.Select(border => new CellBorder(ToPublic(border.Address), ProjectBorder(border.Border))).ToArray(),
            };
        }

        BorderStyle ProjectBorder(Core.Model.BorderStyle border)
        {
            if (!borders.TryGetValue(border, out var projected))
            {
                projected = stylePool.Intern(ToPublic(border));
                borders[border] = projected;
            }

            return projected;
        }
    }

    /// <summary>Maps a cell address.</summary>
    /// <param name="value">The source address.</param>
    /// <returns>The common address.</returns>
    internal static Core.Model.CellAddress ToCore(CellAddress value) => new(value.Row, value.Column);

    /// <summary>Maps a cell address.</summary>
    /// <param name="value">The source address.</param>
    /// <returns>The public address.</returns>
    internal static CellAddress ToPublic(Core.Model.CellAddress value) => new(value.Row, value.Column);

    /// <summary>Maps a cell range.</summary>
    /// <param name="value">The source range.</param>
    /// <returns>The common range.</returns>
    internal static Core.Model.CellRange ToCore(CellRange value) => new(ToCore(value.First), ToCore(value.Last));

    /// <summary>Maps a cell range.</summary>
    /// <param name="value">The source range.</param>
    /// <returns>The public range.</returns>
    internal static CellRange ToPublic(Core.Model.CellRange value) => new(ToPublic(value.First), ToPublic(value.Last));

    /// <summary>Maps source anchors without rounding coordinates.</summary>
    /// <param name="value">The common anchor.</param>
    /// <returns>The public anchor.</returns>
    internal static DrawingAnchor ToPublic(Core.Model.DrawingAnchor value) => new(
        value.Kind switch
        {
            Core.Model.DrawingAnchorKind.Absolute => DrawingAnchorKind.Absolute,
            Core.Model.DrawingAnchorKind.OneCell => DrawingAnchorKind.OneCell,
            Core.Model.DrawingAnchorKind.TwoCell => DrawingAnchorKind.TwoCell,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        },
        value.From is { } from ? ToPublic(from) : null,
        value.FromOffsetX,
        value.FromOffsetY,
        value.To is { } to ? ToPublic(to) : null,
        value.ToOffsetX,
        value.ToOffsetY,
        value.PositionX,
        value.PositionY,
        value.ExtentWidth,
        value.ExtentHeight,
        value.EditAs);

    /// <summary>Restores borrowed full image metadata without copying image bytes.</summary>
    /// <param name="value">The common source image.</param>
    /// <returns>The durable public source image.</returns>
    internal static ReportImage ToPublicImage(Core.Model.ReportImage value)
    {
        if (value.ExtensionData is FullImageData original)
        {
            return original.Image;
        }

        var data = (value.ExtensionData as FullPictureData)?.Metadata;
        return new(value.Anchor is { } anchor ? ToPublic(anchor) : default, value.OffsetX, value.OffsetY, value.Width, value.Height, value.ImageBytes, value.ZIndex, value.Name, value.ContentType, value.Extension)
        {
            DrawingAnchor = value.DrawingAnchor is { } source ? ToPublic(source) : null,
            Crop = data?.Crop,
            Rotation = data?.Rotation ?? 0,
            FlipHorizontal = data?.FlipHorizontal ?? false,
            FlipVertical = data?.FlipVertical ?? false,
        };
    }

    private static HeaderFooter ToPublic(Core.Model.HeaderFooter value) => new(
        ToPublic(value.Header),
        ToPublic(value.Footer),
        value.FirstPageHeader is { } firstHeader ? ToPublic(firstHeader) : null,
        value.FirstPageFooter is { } firstFooter ? ToPublic(firstFooter) : null,
        value.EvenPageHeader is { } evenHeader ? ToPublic(evenHeader) : null,
        value.EvenPageFooter is { } evenFooter ? ToPublic(evenFooter) : null);

    private static HeaderFooterSection ToPublic(Core.Model.HeaderFooterSection value) => new(value.Left, value.Center, value.Right);
}

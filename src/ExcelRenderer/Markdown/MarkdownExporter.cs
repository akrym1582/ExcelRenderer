using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ExcelRenderer.Excel;
using ExcelRenderer.Model;

namespace ExcelRenderer.Markdown;

public sealed class MarkdownExporter
{
    public async Task ExportAsync(ReportDocument document, string outputDirectory,
        MarkdownExportOptions? options = null, string documentName = "workbook.xlsx",
        CancellationToken cancellationToken = default)
    {
        options ??= new MarkdownExportOptions();
        Directory.CreateDirectory(outputDirectory);
        if (options.ExportImages) Directory.CreateDirectory(Path.Combine(outputDirectory, options.ImageDirectoryName));
        var markdown = Build(document, outputDirectory, documentName, options);
        var outputName = Path.GetFileNameWithoutExtension(documentName) + ".md";
        using var writer = new StreamWriter(Path.Combine(outputDirectory, SanitizeFileName(outputName)), false,
            new UTF8Encoding(false));
        await writer.WriteAsync(markdown).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task ExportToFileAsync(ReportDocument document, string outputPath,
        MarkdownExportOptions? options = null, string documentName = "workbook.xlsx",
        CancellationToken cancellationToken = default)
    {
        options ??= new MarkdownExportOptions();
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDirectory);
        if (options.ExportImages) Directory.CreateDirectory(Path.Combine(outputDirectory, options.ImageDirectoryName));
        var markdown = Build(document, outputDirectory, documentName, options);
        using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
        await writer.WriteAsync(markdown).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static string Build(ReportDocument document, string directory, string name, MarkdownExportOptions options)
    {
        var output = new StringBuilder().Append("# ").AppendLine(Escape(Path.GetFileName(name))).AppendLine();
        foreach (var sheet in document.Sheets)
        {
            output.Append("## Sheet: ").AppendLine(Escape(sheet.Name)).AppendLine();
            var cells = new VisualCellBuilder().Build(sheet);
            var root = options.DetectLayout && options.DetectRegions ? new LayoutSegmenter().Segment(cells) :
                new LayoutNode { Cells = cells, BoundingBox = LayoutSegmenter.Bounds(cells) };
            var regions = options.DetectRegions ? new RegionDetector().Detect(root, sheet) :
                cells.Count == 0 ? Array.Empty<SheetRegion>() : new RegionDetector().Detect(root, sheet);
            var index = 1;
            foreach (var region in regions)
            {
                output.Append("### Region ").Append(index++);
                if (options.IncludeCellAddresses)
                    output.Append(" [").Append(Range(region.BoundingRange)).Append(']');
                output.AppendLine().AppendLine();
                WriteRegion(output, region, options);
                output.AppendLine();
            }
            if (options.IncludeHiddenLayoutMetadata) WriteHiddenMetadata(output, sheet);
            WriteImages(output, sheet, cells, regions, directory, options);
        }
        return output.ToString();
    }

    private static void WriteRegion(StringBuilder output, SheetRegion region, MarkdownExportOptions options)
    {
        var cells = region.Cells.Where(c => !string.IsNullOrWhiteSpace(c.Text) ||
            options.IncludeFormula && !string.IsNullOrWhiteSpace(c.Formula)).ToArray();
        if (cells.Length == 0) return;
        if (region.Type == RegionType.Title && cells.All(c => c.Range.First == c.Range.Last))
        {
            output.Append("#### ").AppendLine(Escape(CellText(cells[0], options)));
            return;
        }
        if (region.Type == RegionType.Form && cells.GroupBy(c => c.Range.First.Row).All(r => r.Count() == 2))
        {
            foreach (var row in cells.GroupBy(c => c.Range.First.Row).OrderBy(r => r.Key))
            {
                var pair = row.OrderBy(c => c.X).ToArray();
                output.Append("- **").Append(Escape(pair[0].Text ?? Address(pair[0].Range.First)))
                    .Append(":** ").AppendLine(Escape(CellText(pair[1], options)));
            }
            return;
        }
        var hasMerges = cells.Any(c => c.Range.First != c.Range.Last);
        var rows = cells.GroupBy(c => c.Range.First.Row).OrderBy(r => r.Key).ToArray();
        var rectangular = rows.Length > 1 && rows.Select(r => r.Count()).Distinct().Count() == 1;
        if (hasMerges) WriteHtmlTable(output, cells, options);
        else if (rectangular) WriteMarkdownTable(output, rows, options);
        else WriteRangeList(output, cells, options);
    }

    private static void WriteMarkdownTable(StringBuilder output,
        IGrouping<int, VisualCell>[] rows, MarkdownExportOptions options)
    {
        var ordered = rows.Select(r => r.OrderBy(c => c.Range.First.Column).ToArray()).ToArray();
        output.Append("| ").Append(string.Join(" | ", ordered[0].Select(c => EscapeTable(CellText(c, options)))))
            .AppendLine(" |");
        output.Append("|").Append(string.Join("|", ordered[0].Select(_ => "---"))).AppendLine("|");
        foreach (var row in ordered.Skip(1))
            output.Append("| ").Append(string.Join(" | ", row.Select(c => EscapeTable(CellText(c, options)))))
                .AppendLine(" |");
    }

    private static void WriteHtmlTable(StringBuilder output, IReadOnlyList<VisualCell> cells,
        MarkdownExportOptions options)
    {
        var cellsByAddress = cells.ToDictionary(cell => cell.Range.First);
        var firstRow = cells.Min(cell => cell.Range.First.Row);
        var lastRow = cells.Max(cell => cell.Range.Last.Row);
        var firstColumn = cells.Min(cell => cell.Range.First.Column);
        var lastColumn = cells.Max(cell => cell.Range.Last.Column);
        var occupiedThroughRow = new Dictionary<int, int>();

        output.AppendLine("<table>");
        for (var row = firstRow; row <= lastRow; row++)
        {
            output.AppendLine("<tr>");
            for (var column = firstColumn; column <= lastColumn;)
            {
                if (occupiedThroughRow.TryGetValue(column, out var occupiedRow) && occupiedRow >= row)
                {
                    column++;
                    continue;
                }

                if (!cellsByAddress.TryGetValue(new CellAddress(row, column), out var cell))
                {
                    output.AppendLine("  <td></td>");
                    column++;
                    continue;
                }

                var rowSpan = cell.Range.Last.Row - cell.Range.First.Row + 1;
                var colSpan = cell.Range.Last.Column - cell.Range.First.Column + 1;
                output.Append("  <td");
                if (rowSpan > 1) output.Append(" rowspan=\"").Append(rowSpan).Append('"');
                if (colSpan > 1) output.Append(" colspan=\"").Append(colSpan).Append('"');
                output.Append('>').Append(Html(CellText(cell, options))).AppendLine("</td>");
                for (var spannedColumn = column; spannedColumn <= cell.Range.Last.Column; spannedColumn++)
                    occupiedThroughRow[spannedColumn] = cell.Range.Last.Row;
                column = cell.Range.Last.Column + 1;
            }
            output.AppendLine("</tr>");
        }
        output.AppendLine("</table>");
    }

    private static void WriteRangeList(StringBuilder output, IEnumerable<VisualCell> cells,
        MarkdownExportOptions options)
    {
        if (!options.IncludeCellAddresses)
        {
            foreach (var cell in cells.OrderBy(c => c.Range.First.Row).ThenBy(c => c.Range.First.Column))
                output.Append("- ").AppendLine(Escape(CellText(cell, options)));
            return;
        }
        output.AppendLine("| Range | Text |").AppendLine("|---|---|");
        foreach (var cell in cells.OrderBy(c => c.Range.First.Row).ThenBy(c => c.Range.First.Column))
            output.Append("| ").Append(Range(cell.Range)).Append(" | ")
                .Append(EscapeTable(CellText(cell, options))).AppendLine(" |");
    }

    private static void WriteImages(StringBuilder output, ReportSheet sheet, IReadOnlyList<VisualCell> cells,
        IReadOnlyList<SheetRegion> regions, string directory, MarkdownExportOptions options)
    {
        var images = sheet.Images ?? Array.Empty<ReportImage>();
        if (images.Count == 0) return;
        output.AppendLine("### Images").AppendLine();
        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i];
            var extension = ImageExtension(image);
            var fileName = $"{SanitizeFileName(sheet.Name)}_img_{i + 1:000}.{extension}";
            if (options.ExportImages)
                File.WriteAllBytes(Path.Combine(directory, options.ImageDirectoryName, fileName), image.ImageBytes);
            output.Append("#### Image ").Append(i + 1).AppendLine().AppendLine();
            if (options.IncludeImageMetadata)
            {
                output.Append("- anchor: ").AppendLine(Address(image.Anchor));
                output.Append("- size: ").Append(image.Width.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append("pt x ").Append(image.Height.ToString("0.##", CultureInfo.InvariantCulture)).AppendLine("pt");
                var region = regions.FirstOrDefault(r => r.Images.Contains(image));
                if (region is not null) output.Append("- region: ").AppendLine(Range(region.BoundingRange));
            }
            if (options.IncludeNearbyImageText)
            {
                var nearby = cells.Where(c => Math.Abs(c.Range.First.Row - image.Anchor.Row) <= 2 &&
                    Math.Abs(c.Range.First.Column - image.Anchor.Column) <= 2 && !string.IsNullOrWhiteSpace(c.Text)).Take(5).ToArray();
                if (nearby.Length > 0)
                {
                    output.AppendLine("- nearby_text:");
                    foreach (var cell in nearby) output.Append("  - ").Append(Range(cell.Range)).Append(": \"")
                        .Append(Escape(cell.Text!)).AppendLine("\"");
                }
            }
            output.AppendLine().Append("![Image at ").Append(Address(image.Anchor)).Append("](")
                .Append(Uri.EscapeDataString(options.ImageDirectoryName).Replace("%2F", "/"))
                .Append('/').Append(Uri.EscapeDataString(fileName)).AppendLine(")").AppendLine();
        }
    }

    private static void WriteHiddenMetadata(StringBuilder output, ReportSheet sheet)
    {
        var rows = sheet.Rows.Where(x => x.Value.IsHidden).Select(x => x.Key).OrderBy(x => x).ToArray();
        var columns = sheet.Columns.Where(x => x.Value.IsHidden).Select(x => Address(new CellAddress(1, x.Key)).TrimEnd('1')).ToArray();
        if (rows.Length == 0 && columns.Length == 0) return;
        output.AppendLine("### Layout metadata").AppendLine();
        if (rows.Length > 0) output.Append("- Hidden rows: ").AppendLine(string.Join(", ", rows));
        if (columns.Length > 0) output.Append("- Hidden columns: ").AppendLine(string.Join(", ", columns));
        output.AppendLine();
    }

    private static string CellText(VisualCell cell, MarkdownExportOptions options) =>
        options.IncludeFormula && !string.IsNullOrEmpty(cell.Formula)
            ? string.IsNullOrEmpty(cell.Text) ? cell.Formula! : $"{cell.Text} ({cell.Formula})"
            : cell.Text ?? string.Empty;
    private static string ImageExtension(ReportImage image)
    {
        if (!string.IsNullOrWhiteSpace(image.Extension)) return image.Extension!.TrimStart('.').ToLowerInvariant();
        var b = image.ImageBytes;
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50) return "png";
        if (b.Length >= 3 && b[0] == 0xff && b[1] == 0xd8) return "jpg";
        if (b.Length >= 6 && Encoding.ASCII.GetString(b, 0, 3) == "GIF") return "gif";
        if (b.Length >= 2 && b[0] == 0x42 && b[1] == 0x4d) return "bmp";
        return "bin";
    }
    internal static string Address(CellAddress address)
    {
        var n = address.Column; var letters = string.Empty;
        while (n > 0) { n--; letters = (char)('A' + n % 26) + letters; n /= 26; }
        return letters + address.Row.ToString(CultureInfo.InvariantCulture);
    }
    internal static string Range(CellRange range) => range.First == range.Last
        ? Address(range.First) : Address(range.First) + ":" + Address(range.Last);
    internal static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var cleaned = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim().Trim('.');
        return string.IsNullOrEmpty(cleaned) ? "sheet" : cleaned;
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("`", "\\`")
        .Replace("*", "\\*").Replace("_", "\\_").Replace("[", "\\[").Replace("]", "\\]");
    private static string EscapeTable(string value) => Escape(value).Replace("|", "\\|").Replace("\r", " ").Replace("\n", "<br>");
    private static string Html(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("\r", string.Empty).Replace("\n", "<br>");
}

public static class ExcelMarkdownConverter
{
    public static Task ConvertAsync(string inputPath, string outputDirectory,
        MarkdownExportOptions? options = null, CancellationToken cancellationToken = default)
    {
        var document = new ExcelReader().Read(inputPath);
        return new MarkdownExporter().ExportAsync(document, outputDirectory, options,
            Path.GetFileName(inputPath), cancellationToken);
    }
}

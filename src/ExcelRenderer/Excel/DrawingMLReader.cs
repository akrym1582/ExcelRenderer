using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using S = DocumentFormat.OpenXml.Spreadsheet;
using ExcelRenderer.Model;

namespace ExcelRenderer.Excel;

/// <summary>Reads the DrawingML features that ClosedXML deliberately does not expose.</summary>
internal static class DrawingMLReader
{
    private const double EmusPerPoint = 914400d / 72d;

    public static IReadOnlyDictionary<string, IReadOnlyList<ReportShape>> Read(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart;
        if (workbook?.Workbook.Sheets is null) return new Dictionary<string, IReadOnlyList<ReportShape>>();
        var theme = new ThemeColorResolver(workbook.ThemePart);
        var result = new Dictionary<string, IReadOnlyList<ReportShape>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Workbook.Sheets.Elements<S.Sheet>())
        {
            if (sheet.Id?.Value is not { } id || workbook.GetPartById(id) is not WorksheetPart worksheet) continue;
            var list = new List<ReportShape>();
            var drawing = worksheet.DrawingsPart?.WorksheetDrawing;
            if (drawing is not null)
            {
                var z = 0;
                foreach (var anchor in drawing.ChildElements)
                {
                    foreach (var shape in anchor.Elements<Xdr.Shape>())
                    {
                        var parsed = Parse(shape, anchor, z, theme);
                        if (parsed is not null) list.Add(parsed);
                    }
                    z++;
                }
            }
            result[sheet.Name?.Value ?? string.Empty] = list;
        }
        return result;
    }

    private static ReportShape? Parse(Xdr.Shape shape, OpenXmlElement anchor, int z, ThemeColorResolver theme)
    {
        var preset = shape.ShapeProperties?.GetFirstChild<A.PresetGeometry>()?.Preset?.Value;
        ShapeKind? kind = null;
        if (preset == A.ShapeTypeValues.Rectangle) kind = ShapeKind.Rectangle;
        else if (preset == A.ShapeTypeValues.RoundRectangle) kind = ShapeKind.RoundedRectangle;
        else if (preset == A.ShapeTypeValues.Ellipse) kind = ShapeKind.Ellipse;
        else if (preset == A.ShapeTypeValues.WedgeRectangleCallout) kind = ShapeKind.WedgeRectangleCallout;
        else if (preset == A.ShapeTypeValues.WedgeRoundRectangleCallout) kind = ShapeKind.WedgeRoundedRectangleCallout;
        if (kind is null) return null;

        var (cell, x, y, width, height) = GetBounds(anchor);
        var properties = shape.ShapeProperties;
        var transform = properties?.GetFirstChild<A.Transform2D>();
        if (transform?.Extents is { } extents)
        {
            width = ToPoints(extents.Cx?.Value ?? 0);
            height = ToPoints(extents.Cy?.Value ?? 0);
        }
        var fill = theme.ReadColor(properties?.GetFirstChild<A.SolidFill>())
            ?? theme.ReadStyleColor(shape.ShapeStyle?.FillReference, false);
        var line = properties?.GetFirstChild<A.Outline>();
        var lineColor = theme.ReadColor(line?.GetFirstChild<A.SolidFill>())
            ?? theme.ReadStyleColor(shape.ShapeStyle?.LineReference, true);
        var lineWidth = ToPoints(line?.Width?.Value ?? 12700);
        var rotation = (transform?.Rotation?.Value ?? 0) / 60000d;
        return new(cell, x, y, width, height, kind.Value, new(fill, lineColor, lineWidth),
            ReadText(shape.TextBody, theme), rotation, z, ReadAdjustment(properties));
    }

    private static (CellAddress Cell, double X, double Y, double Width, double Height) GetBounds(OpenXmlElement anchor)
    {
        var from = anchor.GetFirstChild<Xdr.FromMarker>();
        var cell = from is null ? new CellAddress(1, 1) : new(
            (int)(from.RowId?.Text is { } r ? uint.Parse(r) + 1 : 1),
            (int)(from.ColumnId?.Text is { } c ? uint.Parse(c) + 1 : 1));
        var x = ToPoints(from?.ColumnOffset?.Text is { } xo ? long.Parse(xo) : 0);
        var y = ToPoints(from?.RowOffset?.Text is { } yo ? long.Parse(yo) : 0);
        var extent = anchor.GetFirstChild<Xdr.Extent>();
        var absolute = anchor.GetFirstChild<Xdr.Position>();
        if (absolute is not null)
        {
            x = ToPoints(absolute.X?.Value ?? 0); y = ToPoints(absolute.Y?.Value ?? 0);
        }
        return (cell, x, y, ToPoints(extent?.Cx?.Value ?? 0), ToPoints(extent?.Cy?.Value ?? 0));
    }

    private static ShapeText? ReadText(Xdr.TextBody? body, ThemeColorResolver theme)
    {
        if (body is null) return null;
        var text = string.Concat(body.Descendants<A.Text>().Select(x => x.Text));
        if (string.IsNullOrEmpty(text)) return null;
        var run = body.Descendants<A.RunProperties>().FirstOrDefault();
        var paragraph = body.Descendants<A.ParagraphProperties>().FirstOrDefault();
        var props = body.BodyProperties;
        var font = new FontStyle(run?.GetFirstChild<A.LatinFont>()?.Typeface?.Value ?? "Noto Sans JP",
            (run?.FontSize?.Value ?? 1000) / 100d, run?.Bold?.Value ?? false, run?.Italic?.Value ?? false,
            Color: theme.ReadColor(run?.GetFirstChild<A.SolidFill>()));
        var horizontal = paragraph?.Alignment?.Value == A.TextAlignmentTypeValues.Center ? HorizontalAlignment.Center
            : paragraph?.Alignment?.Value == A.TextAlignmentTypeValues.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var vertical = props?.Anchor?.Value == A.TextAnchoringTypeValues.Center ? VerticalAlignment.Center
            : props?.Anchor?.Value == A.TextAnchoringTypeValues.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        return new(text, font, horizontal, vertical, true, ToPoints(props?.LeftInset?.Value ?? 91440),
            ToPoints(props?.TopInset?.Value ?? 45720), ToPoints(props?.RightInset?.Value ?? 91440), ToPoints(props?.BottomInset?.Value ?? 45720));
    }

    private static ShapeAdjustment? ReadAdjustment(Xdr.ShapeProperties? properties)
    {
        var values = properties?.GetFirstChild<A.PresetGeometry>()?.AdjustValueList?.Elements<A.ShapeGuide>()
            .Select(g => g.Formula?.Value?.Split(' ').LastOrDefault()).Where(x => long.TryParse(x, out _)).Select(long.Parse).ToArray();
        return values is { Length: >= 2 } ? new(values[0] / 100000d, values[1] / 100000d) : null;
    }
    private static double ToPoints(long emu) => emu / EmusPerPoint;

    private sealed class ThemeColorResolver
    {
        private readonly ThemePart? _part;
        private readonly Dictionary<string, ReportColor> _colors = new(StringComparer.OrdinalIgnoreCase);

        public ThemeColorResolver(ThemePart? part)
        {
            _part = part;
            var scheme = part?.Theme?.ThemeElements?.ColorScheme;
            if (scheme is null) return;
            foreach (var entry in scheme.ChildElements)
            {
                var color = ReadLiteral(entry.Descendants().FirstOrDefault(IsLiteralColor));
                if (color is not null) _colors[Normalize(entry.LocalName)] = color.Value;
            }
        }

        public ReportColor? ReadColor(A.SolidFill? fill)
        {
            if (fill is null) return null;
            var literal = ReadLiteral(fill.ChildElements.FirstOrDefault(IsLiteralColor));
            if (literal is not null) return literal;
            var scheme = fill.GetFirstChild<A.SchemeColor>()?.Val?.Value.ToString();
            return scheme is null ? null : _colors.GetValueOrDefault(Normalize(scheme));
        }

        public ReportColor? ReadStyleColor(OpenXmlElement? reference, bool line)
        {
            if (reference is null) return null;
            var scheme = reference.GetFirstChild<A.SchemeColor>()?.Val?.Value.ToString();
            if (scheme is not null && !Normalize(scheme).Equals("phclr", StringComparison.OrdinalIgnoreCase) &&
                _colors.TryGetValue(Normalize(scheme), out var referenced)) return referenced;

            var indexText = reference.GetAttribute("idx", string.Empty).Value;
            if (!uint.TryParse(indexText, out var index) || index == 0) return null;
            OpenXmlElement? styles = line
                ? _part?.Theme?.ThemeElements?.FormatScheme?.LineStyleList
                : _part?.Theme?.ThemeElements?.FormatScheme?.FillStyleList;
            var style = styles?.ChildElements.ElementAtOrDefault((int)index - 1);
            return ReadColor(style?.GetFirstChild<A.SolidFill>());
        }

        private static bool IsLiteralColor(OpenXmlElement element) =>
            element is A.RgbColorModelHex || element is A.SystemColor;

        private static ReportColor? ReadLiteral(OpenXmlElement? element)
        {
            var value = element switch
            {
                A.RgbColorModelHex rgb => rgb.Val?.Value,
                A.SystemColor system => system.LastColor?.Value,
                _ => null
            };
            if (value is null || value.Length < 6) return null;
            return new(Convert.ToByte(value.Substring(0, 2), 16), Convert.ToByte(value.Substring(2, 2), 16),
                Convert.ToByte(value.Substring(4, 2), 16));
        }

        private static string Normalize(string value) =>
            new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}

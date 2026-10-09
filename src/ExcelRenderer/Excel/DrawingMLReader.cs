using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Excel;

/// <summary>ClosedXML が公開していない DrawingML の機能を読み取ります。</summary>
internal static class DrawingMLReader
{
    /// <summary>Excel ファイル内の DrawingML を解析し、対応するオートシェイプをワークシート別に読み取ります。</summary>
    /// <param name="path">DrawingML を含む Excel ファイルのパスです。</param>
    /// <returns>ワークシート名をキーとし、描画順に並んだ対応図形を値とする読み取り専用辞書を返します。</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<ReportShape>> Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, null);
    }

    /// <summary>ストリームから DrawingML を読み取り、呼び出し元が所有するストリームを閉じずに処理します。</summary>
    /// <param name="input">読み取り対象の Excel データを含むストリームです。</param>
    /// <returns>ワークシート名をキーとし、描画順に並んだ対応図形を値とする読み取り専用辞書を返します。</returns>
    public static IReadOnlyDictionary<string, IReadOnlyList<ReportShape>> Read(Stream input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        using var copy = new MemoryStream();
        input.CopyTo(copy);
        copy.Position = 0;
        return Read(copy, null);
    }

    /// <summary>読み取り時の診断情報を収集しながら、ストリーム内の DrawingML を解析します。</summary>
    /// <param name="input">読み取り対象の Excel データを含むストリームです。</param>
    /// <param name="diagnostics">解析できない描画オブジェクトの診断情報を追加するコレクターです。</param>
    /// <returns>ワークシート名をキーとし、描画順に並んだ対応図形を値とする読み取り専用辞書を返します。</returns>
    /// <param name="selectedSheets">Sheet bodies to retain; diagnostics still cover every sheet.</param>
    /// <param name="unselectedGeometry">Optional lightweight anchor ranges for unselected-sheet dimension and diagnostic scans.</param>
    internal static IReadOnlyDictionary<string, IReadOnlyList<ReportShape>> Read(Stream input, DiagnosticCollector? diagnostics, IReadOnlyList<string>? selectedSheets = null, IDictionary<string, IReadOnlyList<CellRange>>? unselectedGeometry = null)
    {
        using var document = SpreadsheetDocument.Open(input, false);
        var workbook = document.WorkbookPart;
        if (workbook?.Workbook.Sheets is null)
        {
            return new Dictionary<string, IReadOnlyList<ReportShape>>();
        }

        var theme = new ThemeColorResolver(workbook.ThemePart);
        var result = new Dictionary<string, IReadOnlyList<ReportShape>>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Workbook.Sheets.Elements<S.Sheet>())
        {
            if (sheet.Id?.Value is not { } id || workbook.GetPartById(id) is not WorksheetPart worksheet)
            {
                continue;
            }

            var list = new List<ReportShape>();
            var geometry = new List<CellRange>();
            var drawing = worksheet.DrawingsPart?.WorksheetDrawing;
            if (drawing is not null)
            {
                var z = 0;
                foreach (var anchor in drawing.ChildElements)
                {
                    foreach (var shape in anchor.Elements<Xdr.Shape>())
                    {
                        var selected = selectedSheets is null || selectedSheets.Contains(sheet.Name?.Value, StringComparer.Ordinal);
                        if (GetKind(shape) is not null)
                        {
                            if (selected && Parse(shape, anchor, z, theme) is { } parsed)
                            {
                                list.Add(parsed);
                            }
                            else if (!selected)
                            {
                                // Validate original anchors without allocating text or shape styles.
                                _ = GetBounds(anchor);
                                var source = ReadAnchor(anchor);
                                var from = source.From ?? new CellAddress(1, 1);
                                var to = source.To ?? from;
                                geometry.Add(new(
                                    new(Math.Min(from.Row, to.Row), Math.Min(from.Column, to.Column)),
                                    new(Math.Max(from.Row, to.Row), Math.Max(from.Column, to.Column))));
                            }
                        }
                        else
                        {
                            diagnostics?.Add(
                                new(
                                    "UnsupportedShape",
                                    DiagnosticSeverity.Warning,
                                    DiagnosticStage.Read,
                                    $"DrawingML shape preset '{shape.ShapeProperties?.GetFirstChild<A.PresetGeometry>()?.Preset?.Value.ToString() ?? "unknown"}' is not supported.",
                                    sheet.Name?.Value,
                                    ObjectId: shape.NonVisualShapeProperties?.NonVisualDrawingProperties?.Id?.Value.ToString()));
                        }
                    }

                    foreach (var child in anchor.ChildElements.Where(x => x is Xdr.GraphicFrame or Xdr.GroupShape))
                    {
                        diagnostics?.Add(
                            new(
                                "UnsupportedDrawingObject",
                                DiagnosticSeverity.Warning,
                                DiagnosticStage.Read,
                                $"DrawingML object '{child.LocalName}' is not supported.",
                                sheet.Name?.Value));
                    }

                    z++;
                }
            }

            var name = sheet.Name?.Value ?? string.Empty;
            result[name] = list;
            if (unselectedGeometry is not null)
            {
                unselectedGeometry[name] = geometry;
            }
        }

        return result;
    }

    /// <summary>Reads picture anchor, crop, transform, and drawing-order metadata.</summary>
    /// <param name="input">Workbook stream.</param>
    /// <param name="basicMetadata">Previously read common anchors, when available.</param>
    /// <returns>Picture metadata keyed first by sheet and then by picture name.</returns>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, DrawingPictureMetadata>> ReadPictureMetadata(Stream input, IReadOnlyDictionary<string, IReadOnlyDictionary<string, Core.Excel.DrawingPictureMetadata>>? basicMetadata = null)
    {
        using var document = SpreadsheetDocument.Open(input, false);
        var workbook = document.WorkbookPart;
        var result = new Dictionary<string, IReadOnlyDictionary<string, DrawingPictureMetadata>>(StringComparer.OrdinalIgnoreCase);
        if (workbook?.Workbook.Sheets is null)
        {
            return result;
        }

        foreach (var sheet in workbook.Workbook.Sheets.Elements<S.Sheet>())
        {
            var pictures = new Dictionary<string, DrawingPictureMetadata>(StringComparer.Ordinal);
            if (sheet.Id?.Value is { } id && workbook.GetPartById(id) is WorksheetPart worksheet &&
                worksheet.DrawingsPart?.WorksheetDrawing is { } drawing)
            {
                var z = 0;
                foreach (var anchor in drawing.ChildElements)
                {
                    foreach (var picture in anchor.Elements<Xdr.Picture>())
                    {
                        var name = picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Name?.Value;
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        var transform = picture.ShapeProperties?.Transform2D;
                        var source = picture.BlipFill?.SourceRectangle;
                        var crop = source is null ? null : new ImageCrop(
                            (source.Left?.Value ?? 0) / 100000d,
                            (source.Top?.Value ?? 0) / 100000d,
                            (source.Right?.Value ?? 0) / 100000d,
                            (source.Bottom?.Value ?? 0) / 100000d);
                        var basic = basicMetadata?.GetValueOrDefault(sheet.Name?.Value ?? string.Empty)?.GetValueOrDefault(name);
                        pictures[name] = new(
                            basic is null ? ReadAnchor(anchor) : CoreIntegration.CoreModelAdapter.ToPublic(basic.Anchor),
                            crop,
                            (transform?.Rotation?.Value ?? 0) / 60000d,
                            transform?.HorizontalFlip?.Value ?? false,
                            transform?.VerticalFlip?.Value ?? false,
                            basic?.ZIndex ?? z);
                    }

                    z++;
                }
            }

            result[sheet.Name?.Value ?? string.Empty] = pictures;
        }

        return result;
    }

    private static ShapeKind? GetKind(Xdr.Shape shape)
    {
        var preset = shape.ShapeProperties?.GetFirstChild<A.PresetGeometry>()?.Preset?.Value;
        ShapeKind? kind = null;
        if (preset == A.ShapeTypeValues.Rectangle)
        {
            kind = ShapeKind.Rectangle;
        }
        else if (preset == A.ShapeTypeValues.RoundRectangle)
        {
            kind = ShapeKind.RoundedRectangle;
        }
        else if (preset == A.ShapeTypeValues.Ellipse)
        {
            kind = ShapeKind.Ellipse;
        }
        else if (preset == A.ShapeTypeValues.WedgeRectangleCallout)
        {
            kind = ShapeKind.WedgeRectangleCallout;
        }
        else if (preset == A.ShapeTypeValues.WedgeRoundRectangleCallout)
        {
            kind = ShapeKind.WedgeRoundedRectangleCallout;
        }

        return kind;
    }

    private static ReportShape? Parse(Xdr.Shape shape, OpenXmlElement anchor, int z, ThemeColorResolver theme)
    {
        var kind = GetKind(shape);

        if (kind is null)
        {
            return null;
        }

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
        return new ReportShape(
            cell,
            x,
            y,
            width,
            height,
            kind.Value,
            new(fill, lineColor, lineWidth),
            ReadText(shape.TextBody, theme),
            rotation,
            z,
            ReadAdjustment(properties))
        {
            DrawingAnchor = ReadAnchor(anchor),
        };
    }

    private static DrawingAnchor ReadAnchor(OpenXmlElement anchor) =>
        CoreIntegration.CoreModelAdapter.ToPublic(Core.Excel.DrawingMLReader.ReadAnchor(anchor));

    private static (CellAddress Cell, double X, double Y, double Width, double Height) GetBounds(OpenXmlElement anchor)
    {
        var value = ReadAnchor(anchor);
        var absolute = anchor.GetFirstChild<Xdr.Position>() is not null;
        return (value.From ?? new CellAddress(1, 1), absolute ? value.PositionX : value.FromOffsetX, absolute ? value.PositionY : value.FromOffsetY, value.ExtentWidth, value.ExtentHeight);
    }

    private static ShapeText? ReadText(Xdr.TextBody? body, ThemeColorResolver theme)
    {
        if (body is null)
        {
            return null;
        }

        var text = string.Concat(body.Descendants<A.Text>().Select(x => x.Text));
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var run = body.Descendants<A.RunProperties>().FirstOrDefault();
        var paragraph = body.Descendants<A.ParagraphProperties>().FirstOrDefault();
        var props = body.BodyProperties;
        var font = new FontStyle(
            run?.GetFirstChild<A.LatinFont>()?.Typeface?.Value ?? "Noto Sans JP",
            (run?.FontSize?.Value ?? 1000) / 100d,
            run?.Bold?.Value ?? false,
            run?.Italic?.Value ?? false,
            Color: theme.ReadColor(run?.GetFirstChild<A.SolidFill>()));
        var horizontal = paragraph?.Alignment?.Value == A.TextAlignmentTypeValues.Center ? HorizontalAlignment.Center
            : paragraph?.Alignment?.Value == A.TextAlignmentTypeValues.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        var vertical = props?.Anchor?.Value == A.TextAnchoringTypeValues.Center ? VerticalAlignment.Center
            : props?.Anchor?.Value == A.TextAnchoringTypeValues.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        return new(
            text,
            font,
            horizontal,
            vertical,
            true,
            ToPoints(props?.LeftInset?.Value ?? 91440),
            ToPoints(props?.TopInset?.Value ?? 45720),
            ToPoints(props?.RightInset?.Value ?? 91440),
            ToPoints(props?.BottomInset?.Value ?? 45720));
    }

    private static ShapeAdjustment? ReadAdjustment(Xdr.ShapeProperties? properties)
    {
        var values = properties?.GetFirstChild<A.PresetGeometry>()?.AdjustValueList?.Elements<A.ShapeGuide>()
            .Select(g => g.Formula?.Value?.Split(' ').LastOrDefault()).Where(x => long.TryParse(x, out _)).Select(long.Parse).ToArray();
        return values is { Length: >= 2 } ? new(values[0] / 100000d, values[1] / 100000d) : null;
    }

    private static double ToPoints(long emu) => Core.Excel.DrawingMLReader.ToPoints(emu);

    private sealed class ThemeColorResolver
    {
        private readonly ThemePart? _part;
        private readonly Dictionary<string, ReportColor> _colors = new(StringComparer.OrdinalIgnoreCase);

        public ThemeColorResolver(ThemePart? part)
        {
            _part = part;
            var scheme = part?.Theme?.ThemeElements?.ColorScheme;
            if (scheme is null)
            {
                return;
            }

            foreach (var entry in scheme.ChildElements)
            {
                var color = ReadLiteral(entry.Descendants().FirstOrDefault(IsLiteralColor));
                if (color is not null)
                {
                    _colors[Normalize(entry.LocalName)] = color.Value;
                }
            }
        }

        public ReportColor? ReadColor(A.SolidFill? fill)
        {
            if (fill is null)
            {
                return null;
            }

            var literal = ReadLiteral(fill.ChildElements.FirstOrDefault(IsLiteralColor));
            if (literal is not null)
            {
                return literal;
            }

            var scheme = fill.GetFirstChild<A.SchemeColor>()?.Val?.Value.ToString();
            return scheme is null ? null : _colors.GetValueOrDefault(Normalize(scheme));
        }

        public ReportColor? ReadStyleColor(OpenXmlElement? reference, bool line)
        {
            if (reference is null)
            {
                return null;
            }

            var scheme = reference.GetFirstChild<A.SchemeColor>()?.Val?.Value.ToString();
            if (scheme is not null && !Normalize(scheme).Equals("phclr", StringComparison.OrdinalIgnoreCase) &&
                _colors.TryGetValue(Normalize(scheme), out var referenced))
            {
                return referenced;
            }

            var indexText = reference.GetAttribute("idx", string.Empty).Value;
            if (!uint.TryParse(indexText, out var index) || index == 0)
            {
                return null;
            }

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
                _ => null,
            };
            if (value is null || value.Length < 6)
            {
                return null;
            }

            return new(
                Convert.ToByte(value.Substring(0, 2), 16),
                Convert.ToByte(value.Substring(2, 2), 16),
                Convert.ToByte(value.Substring(4, 2), 16));
        }

        private static string Normalize(string value) =>
            new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}

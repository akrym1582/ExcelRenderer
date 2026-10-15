using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using ExcelRenderer.OpenXml;

/// <summary>Compares XML-present cells and source layout data against ClosedXML without formula evaluation.</summary>
internal static class Comparison
{
    /// <summary>Compares repository fixtures and fails on every unexplained difference or read failure.</summary>
    /// <param name="root">Repository root.</param>
    /// <param name="output">Machine-readable output path.</param>
    /// <returns>Zero only when all differences have explicit supported explanations.</returns>
    internal static int Run(string root, string output)
    {
        var files = Directory.GetFiles(Path.Combine(root, "tests", "ExcelRenderer.Tests", "SampleInputs"), "*.xlsx", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "samples"), "*.xlsx", SearchOption.AllDirectories)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var differences = new List<Dictionary<string, object?>>();
        var results = new List<object>();
        var diagnostics = new List<ReaderDiagnostic>();
        var unexplained = 0;
        var ns = (XNamespace)"http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var path in files)
        {
            var relative = Path.GetRelativePath(root, path);
            var count = 0;
            void Check(string sheet, string? address, string field, object? actual, object? expected, string? accepted = null, string? reason = null)
            {
                var equal = actual is double a && expected is double b ? Math.Abs(a - b) < 1e-9 : Equals(actual, expected);
                if (equal)
                {
                    return;
                }

                differences.Add(new Dictionary<string, object?>
                {
                    ["File"] = relative,
                    ["Sheet"] = sheet,
                    ["Address"] = address,
                    ["Field"] = field,
                    ["Actual"] = actual,
                    ["Expected"] = expected,
                    ["Code"] = accepted ?? "UnexplainedDifference",
                    ["Reason"] = reason,
                });
                if (accepted == null)
                {
                    unexplained++;
                }
            }

            try
            {
                using var stream = File.OpenRead(path);
                using var reader = OpenXmlWorkbookReader.Open(stream, new OpenXmlReaderOptions { Culture = CultureInfo.CurrentCulture, DiagnosticSink = diagnostics.Add });
                using var old = new XLWorkbook(path);
                using var zip = ZipFile.OpenRead(path);
                var rawWorkbook = Load(zip, "xl/workbook.xml");
                var rels = Load(zip, "xl/_rels/workbook.xml.rels");
                var r = (XNamespace)"http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                foreach (var sheet in reader.Sheets.Where(s => s.Kind == "Worksheet"))
                {
                    var rawSheet = rawWorkbook.Root!.Element(ns + "sheets")!.Elements().ElementAt(sheet.Index - 1);
                    var id = (string)rawSheet.Attribute(r + "id")!;
                    var target = (string)rels.Root!.Elements().Single(e => (string?)e.Attribute("Id") == id).Attribute("Target")!;
                    var entry = new Uri(new Uri("http://fixture/xl/workbook.xml"), target).AbsolutePath.TrimStart('/');
                    var raw = Load(zip, entry);
                    var xmlCells = raw.Descendants(ns + "c").ToArray();
                    var info = reader.ReadWorksheetInfo(sheet.Index);
                    var worksheet = old.Worksheet(sheet.Name);
                    Check(sheet.Name, null, "Merges", string.Join(";", info.MergedRanges.Select(m => m.ToString()).OrderBy(v => v, StringComparer.Ordinal)), string.Join(";", worksheet.MergedRanges.Select(m => m.RangeAddress.FirstAddress.ToStringRelative() + ":" + m.RangeAddress.LastAddress.ToStringRelative()).OrderBy(v => v, StringComparer.Ordinal)));
                    foreach (var row in info.Rows)
                    {
                        var source = worksheet.Row(row.Index);
                        if (row.HeightPt.HasValue)
                        {
                            Check(sheet.Name, row.Index.ToString(CultureInfo.InvariantCulture), "RowHeightPt", row.HeightPt.Value, source.Height);
                        }

                        Check(sheet.Name, row.Index.ToString(CultureInfo.InvariantCulture), "RowHidden", row.Hidden ?? false, source.IsHidden);
                    }

                    foreach (var column in info.Columns)
                    {
                        // Endpoints suffice for a saved interval; do not expand the grid area.
                        foreach (var c in new[] { column.Min, column.Max }.Distinct())
                        {
                            var source = worksheet.Column(c);

                            // ClosedXML 0.105.1 LoadColumns subtracts XLConstants.ColumnWidthOffset.
                            if (column.Width.HasValue)
                            {
                                Check(sheet.Name, c.ToString(CultureInfo.InvariantCulture), "ColumnWidth", column.Width.Value, source.Width + 0.710625);
                            }

                            Check(sheet.Name, c.ToString(CultureInfo.InvariantCulture), "ColumnHidden", column.Hidden ?? false, source.IsHidden);
                        }
                    }

                    ComparePrint(info, worksheet, (field, actual, expected) => Check(sheet.Name, null, field, actual, expected));
                    var offset = 0;
                    foreach (var cell in reader.EnumerateCells(sheet.Index))
                    {
                        if (offset >= xmlCells.Length)
                        {
                            throw new InvalidDataException("Reader returned more cells than the XML.");
                        }

                        var xml = xmlCells[offset++];
                        var address = cell.Address.ToString();
                        var source = worksheet.Cell(cell.Address.Row, cell.Address.Column);
                        var cached = source.CachedValue;
                        Check(sheet.Name, address, "RawValue", cell.RawValue, xml.Element(ns + "v")?.Value);
                        if (xml.Attribute("r") != null)
                        {
                            Check(sheet.Name, address, "Address", address, (string?)xml.Attribute("r"));
                        }

                        if (cell.Formula == null)
                        {
                            switch (cell.Value.Kind)
                            {
                                case "String":
                                    Check(sheet.Name, address, "String", cell.Value.Text, cached.IsBlank ? string.Empty : cached.GetText());
                                    break;
                                case "Number":
                                    var number = cached.IsDateTime ? cached.GetDateTime().ToOADate() - (old.Use1904DateSystem ? 1462 : 0) : cached.IsTimeSpan ? cached.GetTimeSpan().TotalDays : cached.GetNumber();
                                    Check(sheet.Name, address, "Number", cell.Value.Number, number);
                                    break;
                                case "Boolean":
                                    Check(sheet.Name, address, "Boolean", cell.Value.Boolean, cached.GetBoolean());
                                    break;
                                case "Error":
                                    Check(sheet.Name, address, "Error", cell.Value.Text, cached.ToString(CultureInfo.InvariantCulture));
                                    break;
                                case "Blank":
                                    Check(sheet.Name, address, "Blank", true, cached.IsBlank);
                                    break;
                            }
                        }
                        else
                        {
                            // Cache presence and type come from the original XML, never Value/GetFormattedString.
                            var hasCache = xml.Element(ns + "v") != null || xml.Element(ns + "is") != null;
                            Check(sheet.Name, address, "FormulaHasCache", cell.Formula.HasCachedResult, hasCache);
                            if (xml.Element(ns + "f") is { } formula)
                            {
                                Check(sheet.Name, address, "RawFormula", cell.Formula.RawFormula, formula.Value);
                            }
                        }

                        var style = reader.ResolveStyle(cell);
                        var fontName = Child(style.Font, "name", "val");
                        if (fontName != null)
                        {
                            Check(sheet.Name, address, "FontName", fontName, source.Style.Font.FontName);
                        }

                        var size = Child(style.Font, "sz", "val");
                        if (size != null)
                        {
                            Check(sheet.Name, address, "FontSize", double.Parse(size, CultureInfo.InvariantCulture), source.Style.Font.FontSize);
                        }

                        Check(sheet.Name, address, "FontBold", Flag(style.Font, "b"), source.Style.Font.Bold);
                        Check(sheet.Name, address, "FontItalic", Flag(style.Font, "i"), source.Style.Font.Italic);
                        Check(sheet.Name, address, "FontStrike", Flag(style.Font, "strike"), source.Style.Font.Strikethrough);
                        var underline = style.Font.Children.FirstOrDefault(c => c.Name == "u");
                        Check(sheet.Name, address, "FontUnderline", underline == null ? "none" : underline.Attributes.GetValueOrDefault("val", "single").ToLowerInvariant(), source.Style.Font.Underline.ToString().ToLowerInvariant());
                        CompareColor(style.Font.Children.FirstOrDefault(c => c.Name == "color"), source.Style.Font.FontColor, (field, a, b) => Check(sheet.Name, address, "FontColor." + field, a, b));
                        var pattern = style.Fill.Children.FirstOrDefault(c => c.Name == "patternFill");
                        if (pattern != null)
                        {
                            Check(sheet.Name, address, "FillPattern", pattern.Attributes.GetValueOrDefault("patternType", "none").ToLowerInvariant(), source.Style.Fill.PatternType.ToString().ToLowerInvariant());
                            CompareColor(pattern.Children.FirstOrDefault(c => c.Name == "fgColor"), source.Style.Fill.BackgroundColor, (field, a, b) => Check(sheet.Name, address, "FillColor." + field, a, b));
                        }

                        var border = source.Style.Border;
                        foreach (var side in new[]
                        {
                            ("left", border.LeftBorder, border.LeftBorderColor),
                            ("right", border.RightBorder, border.RightBorderColor),
                            ("top", border.TopBorder, border.TopBorderColor),
                            ("bottom", border.BottomBorder, border.BottomBorderColor),
                            ("diagonal", border.DiagonalBorder, border.DiagonalBorderColor),
                        })
                        {
                            var saved = style.Border.Children.FirstOrDefault(c => c.Name == side.Item1);
                            Check(sheet.Name, address, "Border." + side.Item1, saved?.Attributes.GetValueOrDefault("style", "none").ToLowerInvariant() ?? "none", side.Item2.ToString().ToLowerInvariant());
                            CompareColor(saved?.Children.FirstOrDefault(c => c.Name == "color"), side.Item3, (field, a, b) => Check(sheet.Name, address, "BorderColor." + side.Item1 + "." + field, a, b));
                        }

                        if (style.Alignment != null)
                        {
                            var a = style.Alignment.Attributes;
                            var oldAlignment = source.Style.Alignment;
                            if (a.TryGetValue("horizontal", out var horizontal))
                            {
                                Check(sheet.Name, address, "HorizontalAlignment", horizontal.ToLowerInvariant(), oldAlignment.Horizontal.ToString().ToLowerInvariant());
                            }

                            if (a.TryGetValue("vertical", out var vertical))
                            {
                                Check(sheet.Name, address, "VerticalAlignment", vertical.ToLowerInvariant(), oldAlignment.Vertical.ToString().ToLowerInvariant());
                            }

                            if (a.TryGetValue("wrapText", out var wrap))
                            {
                                Check(sheet.Name, address, "WrapText", wrap == "1" || wrap == "true", oldAlignment.WrapText);
                            }

                            if (a.TryGetValue("shrinkToFit", out var shrink))
                            {
                                Check(sheet.Name, address, "ShrinkToFit", shrink == "1" || shrink == "true", oldAlignment.ShrinkToFit);
                            }

                            if (a.TryGetValue("textRotation", out var rotation))
                            {
                                var angle = int.Parse(rotation, CultureInfo.InvariantCulture);
                                Check(sheet.Name, address, "TextRotation", angle == 255 ? 255 : angle > 90 ? 90 - angle : angle, oldAlignment.TextRotation);
                            }
                        }

                        var format = reader.FormatCell(cell);
                        if (cell.Formula == null)
                        {
                            string? accepted = null;
                            string? reason = null;
                            if (format.Diagnostics.Any(d => d.Code == "UnsupportedNumberFormat"))
                            {
                                accepted = "UnsupportedNumberFormat";
                                reason = "The original format is retained; this reader deliberately uses General for unverified locale-specific formats.";
                            }
                            else if (style.NumberFormatId == 14 && style.NumberFormatCode == "mm-dd-yy")
                            {
                                accepted = "BuiltinDate14";
                                reason = "Reader uses the explicit OOXML built-in code mm-dd-yy; ClosedXML localizes built-in 14.";
                            }

                            Check(sheet.Name, address, "Display", format.Text, source.GetFormattedString(CultureInfo.CurrentCulture), accepted, reason);
                        }

                        count++;
                    }

                    Check(sheet.Name, null, "XmlPresentCellCount", offset, xmlCells.Length);
                }

                results.Add(new { File = relative, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), Cells = count, Status = "Read" });
                Console.WriteLine($"{relative}: {count} cells");
            }
            catch (Exception e)
            {
                Check(string.Empty, null, "ReadFailure", e.GetType().Name + ": " + e.Message, "Successful read");
                results.Add(new { File = relative, Cells = count, Status = "Failed" });
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(
            output,
            JsonSerializer.Serialize(
                new
                {
                    Environment = Evidence.Capture(),
                    Culture = CultureInfo.CurrentCulture.Name,
                    Files = results,
                    Differences = differences,
                    UnexplainedDifferences = unexplained,
                    Diagnostics = diagnostics,
                    FormulaPolicy = "Original XML cache; no formula evaluation or shared-reference rewriting.",
                },
                new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Unexplained: {unexplained}; explained: {differences.Count - unexplained}; diagnostics: {diagnostics.Count}");
        return unexplained == 0 ? 0 : 1;
    }

    private static XDocument Load(ZipArchive zip, string entry)
    {
        using var stream = zip.GetEntry(entry)!.Open();
        return XDocument.Load(stream, System.Xml.Linq.LoadOptions.PreserveWhitespace);
    }

    private static string? Child(ElementInfo parent, string child, string attribute)
    {
        var element = parent.Children.FirstOrDefault(c => c.Name == child);
        return element != null && element.Attributes.TryGetValue(attribute, out var value) ? value : null;
    }

    private static bool Flag(ElementInfo parent, string child)
    {
        var element = parent.Children.FirstOrDefault(c => c.Name == child);
        return element != null && (!element.Attributes.TryGetValue("val", out var value) || value == "1" || value == "true");
    }

    private static void CompareColor(ElementInfo? color, XLColor old, Action<string, object?, object?> check)
    {
        if (color == null || color.Attributes.ContainsKey("auto"))
        {
            return;
        }

        if (color.Attributes.TryGetValue("theme", out var theme))
        {
            check("Theme", int.Parse(theme, CultureInfo.InvariantCulture), old.ColorType == XLColorType.Theme ? (int)old.ThemeColor : -1);
            check("Tint", color.Attributes.TryGetValue("tint", out var tint) ? double.Parse(tint, CultureInfo.InvariantCulture) : 0.0, old.ColorType == XLColorType.Theme ? old.ThemeTint : 0.0);
        }
        else if (color.Attributes.TryGetValue("indexed", out var indexed))
        {
            check("Indexed", int.Parse(indexed, CultureInfo.InvariantCulture), old.ColorType == XLColorType.Indexed ? old.Indexed : -1);
        }
        else if (color.Attributes.TryGetValue("rgb", out var rgb))
        {
            check("Rgb", rgb.Substring(rgb.Length - 6).ToUpperInvariant(), old.ColorType == XLColorType.Color ? (old.Color.ToArgb() & 0xffffff).ToString("X6", CultureInfo.InvariantCulture) : old.ColorType.ToString());
        }
    }

    private static void ComparePrint(WorksheetInfo info, IXLWorksheet worksheet, Action<string, object?, object?> check)
    {
        void Margin(string attribute, double expected)
        {
            if (info.PageMargins?.Attributes.TryGetValue(attribute, out var value) == true)
            {
                check("MarginInch." + attribute, double.Parse(value, CultureInfo.InvariantCulture), expected);
            }
        }

        Margin("left", worksheet.PageSetup.Margins.Left);
        Margin("right", worksheet.PageSetup.Margins.Right);
        Margin("top", worksheet.PageSetup.Margins.Top);
        Margin("bottom", worksheet.PageSetup.Margins.Bottom);
        Margin("header", worksheet.PageSetup.Margins.Header);
        Margin("footer", worksheet.PageSetup.Margins.Footer);
        if (info.PageSetup?.Attributes.TryGetValue("scale", out var scale) == true && info.PageSetupProperties?.Attributes.GetValueOrDefault("fitToPage") != "1")
        {
            check("Scale", int.Parse(scale, CultureInfo.InvariantCulture), worksheet.PageSetup.Scale);
        }

        if (info.PageSetup?.Attributes.TryGetValue("fitToWidth", out var width) == true)
        {
            check("FitToWidth", int.Parse(width, CultureInfo.InvariantCulture), worksheet.PageSetup.PagesWide);
        }

        var areas = info.PrintReferences.Where(p => p.Name == "_xlnm.Print_Area").SelectMany(p => p.Ranges).Where(r => r.Kind == "Cells").Select(r => new CellRange(new CellAddress(r.FirstRow, r.FirstColumn), new CellAddress(r.LastRow, r.LastColumn)).ToString());
        var oldAreas = worksheet.PageSetup.PrintAreas.Select(r => r.RangeAddress.FirstAddress.ToStringRelative() + ":" + r.RangeAddress.LastAddress.ToStringRelative());
        check("PrintAreas", string.Join(";", areas), string.Join(";", oldAreas));
    }
}

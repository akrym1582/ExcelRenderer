using System.Security.Cryptography;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Rendering;
using ExcelRenderer.TestFixtures;
using UglyToad.PdfPig;
using Xunit;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Tests;

public sealed class MainCoreParityTests
{
    internal static string FontPath => Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf");
    internal static string EvidenceDirectory => Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "MainCoreParity");

    public static TheoryData<string> CommonCases => new()
    {
        "values", "text", "dimensions-match", "dimensions-mismatch", "borders", "portrait", "landscape", "scale", "fit", "breaks", "titles", "areas", "empty", "selection", "images", "header-footer", "defaults", "border-conflict", "image-overlap",
    };

    [Theory]
    [MemberData(nameof(CommonCases))]
    public async Task Public_apis_agree_on_common_contract_with_independent_expectations(string id)
    {
        var bytes = Fixture(id);
        var sheets = id == "selection" ? new[] { "second", "data" } : new[] { "data" };
        var (main, core, mainResult, coreResult) = await Render(bytes, sheets);
        Directory.CreateDirectory(EvidenceDirectory);
        await File.WriteAllBytesAsync(Path.Combine(EvidenceDirectory, id + ".xlsx"), bytes);
        await File.WriteAllBytesAsync(Path.Combine(EvidenceDirectory, id + "-main.pdf"), main);
        await File.WriteAllBytesAsync(Path.Combine(EvidenceDirectory, id + "-core.pdf"), core);
        try
        {
            Compare(main, core);
            using var pdf = PdfDocument.Open(main);
            var text = string.Concat(pdf.GetPages().Select(page => page.Text));
            if (id == "empty") Assert.Equal(1, pdf.NumberOfPages);
            else Assert.Contains("共通 ABC", text);
            Assert.DoesNotContain("EXCLUDED", text);
            Assert.DoesNotContain("HIDDEN", text);
            if (id == "values")
            {
                foreach (var expected in new[] { "123", "12.50", "-42", "2024-01-01", "saved", "TRUE", "#DIV/0!" }) Assert.Contains(expected, text);
                Assert.Contains(mainResult.Diagnostics, d => d.Code == "FormulaCachedValueMissing");
                Assert.Contains(coreResult.Diagnostics, d => d.Code == "FormulaCachedValueMissing");
            }
            if (id == "selection")
            {
                Assert.Equal(2, pdf.NumberOfPages);
                Assert.Contains("SECOND", pdf.GetPage(1).Text);
                Assert.Contains("共通 ABC", pdf.GetPage(2).Text);
            }
            if (id == "breaks" || id == "titles" || id == "areas") Assert.Equal(2, pdf.NumberOfPages);
            if (id == "titles") Assert.All(pdf.GetPages(), page => Assert.Contains("共通 ABC", page.Text));
            if (id == "header-footer")
            {
                Assert.Contains("STATIC", pdf.GetPage(1).Text);
                Assert.Contains("PAGE 1", pdf.GetPage(1).Text);
            }
            if (id == "scale") Assert.All(pdf.GetPage(1).Letters, letter => Near(8.25, letter.FontSize));
            if (id == "fit")
            {
                Assert.Equal(1, pdf.NumberOfPages);
                Assert.All(pdf.GetPage(1).Letters, letter => Assert.True(letter.FontSize < 11));
            }
            if (id == "images") Assert.Single(pdf.GetPage(1).GetImages());
            if (id == "image-overlap") Assert.Equal(2, pdf.GetPage(1).GetImages().Count());
            if (id == "defaults")
            {
                Assert.Contains("ROW", text); Assert.Contains("COLUMN", text);
                Assert.Contains(pdf.GetPage(1).Paths, path => path.IsFilled && path.FillColor?.ToRGBValues() == (1d, 1d, 0d));
            }
            if (id == "border-conflict") Assert.Contains(pdf.GetPage(1).Paths, path => path.IsStroked && Math.Abs(path.LineWidth - 0.75) < 0.001);
            if (id is "portrait" or "landscape")
            {
                // A4 is 210 x 297 mm; PDF points are 72 / 25.4 per mm.
                Assert.InRange(pdf.GetPage(1).Width, (id == "portrait" ? 595.276 : 841.89) - 0.02, (id == "portrait" ? 595.276 : 841.89) + 0.02);
            }
            if (id == "dimensions-match")
            {
                // Left margin = 0.5 inch = 36 pt; no product layout calculation is used.
                Assert.InRange(pdf.GetPage(1).Letters[0].StartBaseLine.X, 36, 40);
            }
            if (id == "borders")
            {
                Assert.Contains(pdf.GetPage(1).Paths, path => path.IsFilled && path.FillColor?.ToRGBValues() == (1d, 1d, 0d));
                Assert.Contains(pdf.GetPage(1).Paths, path => path.IsStroked && Math.Abs(path.LineWidth - 0.5) < 0.001);
            }
            if (id == "text")
            {
                Assert.True(pdf.GetPage(1).Letters.Where(letter => letter.FontSize == 14).Select(letter => Math.Round(letter.StartBaseLine.Y, 2)).Distinct().Count() >= 2);
                Assert.Contains(pdf.GetPage(1).Letters, letter => letter.FontSize < 11);
                Assert.Contains(pdf.GetPage(1).Paths, path => path.IsFilled || path.IsStroked);
            }
            AssertFonts(main);
            AssertFonts(core);
            await Report(id, bytes, "passed", null);
        }
        catch (Exception error)
        {
            await Report(id, bytes, "failed", error.Message);
            throw;
        }
    }

    [Fact]
    public async Task Comparator_rejects_missing_text_shifted_text_missing_duplicate_border_and_extra_page()
    {
        var (original, _, _, _) = await Render(Fixture("borders"), ["data"]);
        foreach (var mutation in new[] { "missing-text", "shift", "missing-border", "duplicate-border", "extra-page" })
        {
            var bytes = Fixture("borders");
            using var stream = new MemoryStream();
            stream.Write(bytes);
            using (var workbook = SpreadsheetDocument.Open(stream, true))
            {
                var sheet = workbook.WorkbookPart!.WorksheetParts.First().Worksheet;
                if (mutation == "missing-text") sheet.Descendants<S.Text>().First().Text = "";
                if (mutation == "shift") sheet.GetFirstChild<S.PageMargins>()!.Left = 0.75;
                if (mutation == "missing-border") workbook.WorkbookPart.WorkbookStylesPart!.Stylesheet.Borders!.ChildElements[1].RemoveAllChildren();
                if (mutation == "duplicate-border") sheet.GetFirstChild<S.SheetData>()!.Append(new S.Row(SdkWorkbook.Text("A5", "", 1)) { RowIndex = 5 });
                if (mutation == "extra-page") sheet.Append(new S.RowBreaks(new S.Break { Id = 1, Min = 0, Max = 16383, ManualPageBreak = true }) { Count = 1, ManualBreakCount = 1 });
            }
            var (changed, _, _, _) = await Render(stream.ToArray(), ["data"]);
            Assert.ThrowsAny<Exception>(() => Compare(original, changed));
        }
    }

    internal static void Compare(byte[] left, byte[] right)
    {
        using var a = PdfDocument.Open(left, new ParsingOptions { UseLenientParsing = false });
        using var b = PdfDocument.Open(right, new ParsingOptions { UseLenientParsing = false });
        Assert.Equal(a.NumberOfPages, b.NumberOfPages);
        for (var index = 1; index <= a.NumberOfPages; index++)
        {
            var x = a.GetPage(index);
            var y = b.GetPage(index);
            Near(x.Width, y.Width); Near(x.Height, y.Height);
            Assert.Equal(x.Text, y.Text);
            Assert.Equal(x.Letters.Count, y.Letters.Count);
            foreach (var (l, r) in x.Letters.Zip(y.Letters))
            {
                Assert.Equal(l.Value, r.Value);
                Near(l.StartBaseLine.X, r.StartBaseLine.X); Near(l.StartBaseLine.Y, r.StartBaseLine.Y);
                Near(l.EndBaseLine.X, r.EndBaseLine.X); Near(l.EndBaseLine.Y, r.EndBaseLine.Y);
                Near(l.FontSize, r.FontSize);
                Assert.Equal(l.Color.ToRGBValues(), r.Color.ToRGBValues());
            }
            // Keep drawing order: overlapping fill/border mutations must not disappear in a set.
            var painted = x.Paths.Where(path => path.IsFilled || path.IsStroked).ToArray();
            var otherPainted = y.Paths.Where(path => path.IsFilled || path.IsStroked).ToArray();
            Assert.Equal(painted.Length, otherPainted.Length);
            foreach (var (l, r) in painted.Zip(otherPainted))
            {
                Assert.Equal(l.IsFilled, r.IsFilled); Assert.Equal(l.IsStroked, r.IsStroked);
                Near(l.LineWidth, r.LineWidth);
                Assert.Equal(l.FillColor?.ToRGBValues(), r.FillColor?.ToRGBValues());
                Assert.Equal(l.StrokeColor?.ToRGBValues(), r.StrokeColor?.ToRGBValues());
                Assert.Equal(l.Count, r.Count);
                foreach (var (ls, rs) in l.Zip(r))
                {
                    Assert.Equal(ls.Commands.Count, rs.Commands.Count);
                    foreach (var (lc, rc) in ls.Commands.Zip(rs.Commands)) Assert.Equal(lc.ToString(), rc.ToString());
                }
            }
            var images = x.GetImages().ToArray();
            var other = y.GetImages().ToArray();
            Assert.Equal(images.Length, other.Length);
            foreach (var (l, r) in images.Zip(other))
            {
                Assert.Equal(l.BoundingBox, r.BoundingBox);
                Assert.Equal(l.WidthInSamples, r.WidthInSamples); Assert.Equal(l.HeightInSamples, r.HeightInSamples);
                Assert.Equal(l.RawBytes.ToArray(), r.RawBytes.ToArray());
            }
        }
    }

    internal static async Task<(byte[] Main, byte[] Core, ConversionResult MainResult, Core.ConversionResult CoreResult)> Render(byte[] bytes, string[]? sheets)
    {
        using var mainInput = new MemoryStream(bytes);
        using var coreInput = new MemoryStream(bytes);
        using var main = new MemoryStream();
        using var core = new MemoryStream();
        var result = await ExcelConverter.RenderAsync(mainInput, new()
        {
            OutputFormat = OutputFormat.Pdf,
            Selection = new() { SheetNames = sheets },
            ReadOptions = new() { FormulaDisplayMode = Excel.FormulaDisplayMode.CachedValue },
            FontOptions = new() { FontFiles = [FontPath], FontFileCollectionIndex = 0, AllowSystemFonts = false, UseFontPack = false, Policy = Fonts.FontPolicy.PreferRequested, FallbackFamilies = ["Noto Sans JP"] },
        }, new SingleStreamOutputSink(main));
        var basic = await Core.ExcelConverter.ConvertAsync(coreInput, core, new() { FontFilePath = FontPath, FontCollectionIndex = 0, SheetNames = sheets });
        Assert.True(mainInput.CanRead && coreInput.CanRead && main.CanWrite && core.CanWrite);
        return (main.ToArray(), core.ToArray(), result, basic);
    }

    internal static byte[] Fixture(string id)
    {
        var styles = SdkWorkbook.Styles(new S.CellFormat(), new S.CellFormat { BorderId = 1, FillId = 2 }, new S.CellFormat { NumberFormatId = 2 }, new S.CellFormat { NumberFormatId = 164 }, new S.CellFormat(new S.Alignment { WrapText = true, Horizontal = S.HorizontalAlignmentValues.Center, Vertical = S.VerticalAlignmentValues.Center }) { FontId = 1 }, new S.CellFormat(new S.Alignment { ShrinkToFit = true, Horizontal = S.HorizontalAlignmentValues.Right, Vertical = S.VerticalAlignmentValues.Top }));
        styles.Fonts!.GetFirstChild<S.Font>()!.FontName!.Val = id == "dimensions-mismatch" ? "Calibri" : "Noto Sans JP";
        styles.Fonts.Append(new S.Font(new S.FontSize { Val = 14 }, new S.Color { Rgb = "FF0000FF" }, new S.FontName { Val = "Noto Sans JP" }, new S.Underline()));
        styles.Fills!.Append(new S.Fill(new S.PatternFill(new S.ForegroundColor { Rgb = "FFFFFF00" }) { PatternType = S.PatternValues.Solid }));
        styles.Borders!.Append(new S.Border(new S.LeftBorder(new S.Color { Rgb = "FFFF0000" }) { Style = S.BorderStyleValues.Thin }, new S.RightBorder { Style = S.BorderStyleValues.Double }, new S.TopBorder { Style = S.BorderStyleValues.Dashed }, new S.BottomBorder { Style = S.BorderStyleValues.Thin }));
        styles.Borders.Append(new S.Border(new S.LeftBorder { Style = S.BorderStyleValues.Thin }, new S.RightBorder { Style = S.BorderStyleValues.Thin }, new S.TopBorder { Style = S.BorderStyleValues.Double }, new S.BottomBorder { Style = S.BorderStyleValues.Thin }));
        styles.CellFormats!.Append(new S.CellFormat { BorderId = 2 });
        styles.InsertAt(new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy-mm-dd" }), 0);
        var rows = new S.SheetData();
        if (id != "empty")
        {
            rows.Append(new S.Row(SdkWorkbook.Text("A1", id == "text" ? "共通 ABC 共通 ABC 共通 ABC" : "共通 ABC", id == "text" ? 4U : id is "borders" or "border-conflict" ? 1U : 0U)) { RowIndex = 1, Height = id == "text" ? 60 : 30, CustomHeight = true });
            rows.Append(new S.Row(SdkWorkbook.Text("A2", id == "text" ? "repeat repeat repeat repeat repeat repeat" : "repeat repeat", id == "text" ? 5U : id == "border-conflict" ? 6U : id == "borders" ? 1U : 0U)) { RowIndex = 2, Height = 30, CustomHeight = true });
            rows.Elements<S.Row>().First().Append(SdkWorkbook.Text("D1", "HIDDEN-COLUMN"));
            if (id == "defaults")
            {
                var columnCell = SdkWorkbook.Text("C1", "COLUMN");
                columnCell.StyleIndex = null;
                rows.Elements<S.Row>().First().InsertBefore(columnCell, rows.Elements<S.Row>().First().Elements<S.Cell>().Last());
                var rowCell = SdkWorkbook.Text("A3", "ROW");
                rowCell.StyleIndex = null;
                rows.Append(new S.Row(rowCell) { RowIndex = 3, StyleIndex = 1, CustomFormat = true });
            }
            if (id == "values")
            {
                rows.Append(new S.Row(SdkWorkbook.Number("A3", "123"), SdkWorkbook.Number("B3", "12.5", 2), SdkWorkbook.Number("C3", "-42")) { RowIndex = 3 });
                rows.Append(new S.Row(SdkWorkbook.Number("A4", "45292", 3), Formula("B4", "str", "saved"), Formula("C4", "b", "1")) { RowIndex = 4 });
                rows.Append(new S.Row(Formula("A5", "e", "#DIV/0!"), Formula("B5", null, null)) { RowIndex = 5 });
            }
            if (id.StartsWith("dimensions", StringComparison.Ordinal)) rows.Append(new S.Row(SdkWorkbook.Text("A3", "HIDDEN")) { RowIndex = 3, Hidden = true });
        }
        if (id == "fit")
        {
            foreach (var row in rows.Elements<S.Row>()) row.Height = 500;
        }
        var sheet = new S.Worksheet(new S.SheetProperties(new S.PageSetupProperties { FitToPage = id == "fit" }), new S.Columns(new S.Column { Min = 1, Max = 2, Width = 20, CustomWidth = true }, new S.Column { Min = 3, Max = 3, Width = 20, CustomWidth = true, Style = id == "defaults" ? 1U : 0U }, new S.Column { Min = 4, Max = 4, Hidden = true }), rows);
        if (id.StartsWith("dimensions", StringComparison.Ordinal) || id == "borders") sheet.Append(new S.MergeCells(new S.MergeCell { Reference = "A1:B1" }));
        sheet.Append(new S.PageMargins { Left = 0.5, Right = 0.5, Top = 0.5, Bottom = 0.5, Header = 0.2, Footer = 0.2 });
        sheet.Append(new S.PageSetup { PaperSize = 9, Orientation = id == "landscape" ? S.OrientationValues.Landscape : S.OrientationValues.Portrait, Scale = id == "scale" ? 75U : 100U, FitToWidth = 1, FitToHeight = 1 });
        if (id == "header-footer") sheet.Append(new S.HeaderFooter(new S.OddHeader("&CSTATIC"), new S.OddFooter("&CPAGE &P")));
        if (id is "breaks" or "titles") sheet.Append(new S.RowBreaks(new S.Break { Id = 1, Min = 0, Max = 16383, ManualPageBreak = true }) { Count = 1, ManualBreakCount = 1 });
        using var stream = SdkWorkbook.Create(sheet, styles);
        using (var workbook = SpreadsheetDocument.Open(stream, true))
        {
            if (id == "selection")
            {
                SdkWorkbook.AddSheet(workbook.WorkbookPart!, new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "SECOND")) { RowIndex = 1 })), "second");
                SdkWorkbook.AddSheet(workbook.WorkbookPart!, new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "EXCLUDED")) { RowIndex = 1 })), "excluded");
            }
            if (id is "images" or "image-overlap")
            {
                using var bitmap = new global::SkiaSharp.SKBitmap(3, 3);
                bitmap.Erase(global::SkiaSharp.SKColors.Red);
                using var image = global::SkiaSharp.SKImage.FromBitmap(bitmap);
                using var data = image.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
                SdkWorkbook.Image(workbook.WorkbookPart!.WorksheetParts.First(), data.ToArray(), row: 1, column: 1);
                if (id == "image-overlap")
                {
                    bitmap.Erase(global::SkiaSharp.SKColors.Blue);
                    using var blue = global::SkiaSharp.SKImage.FromBitmap(bitmap);
                    using var blueData = blue.Encode(global::SkiaSharp.SKEncodedImageFormat.Png, 100);
                    SdkWorkbook.Image(workbook.WorkbookPart.WorksheetParts.First(), blueData.ToArray(), "front", row: 1, column: 1);
                }
            }
            if (id is "titles" or "areas") workbook.WorkbookPart!.Workbook.Append(new S.DefinedNames(new S.DefinedName { Name = id == "titles" ? "_xlnm.Print_Titles" : "_xlnm.Print_Area", LocalSheetId = 0, Text = id == "titles" ? "'data'!$1:$1" : "'data'!$A$1:$A$1,'data'!$A$2:$A$2" }));
        }
        return stream.ToArray();
    }

    private static S.Cell Formula(string address, string? type, string? value)
    {
        var cell = new S.Cell(new S.CellFormula("1/0")) { CellReference = address };
        if (type is not null) cell.DataType = type == "str" ? S.CellValues.String : type == "b" ? S.CellValues.Boolean : S.CellValues.Error;
        if (value is not null) cell.Append(new S.CellValue(value));
        return cell;
    }

    private static void Near(double a, double b) => Assert.InRange(Math.Abs(a - b), 0, 0.01);

    private static void AssertFonts(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var pdf = PdfTestFile.Open(input);
        using var expected = global::SkiaSharp.SKTypeface.FromFile(FontPath);
        foreach (var page in pdf.Pages)
        {
            var fonts = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font");
            if (fonts is null) continue;
            foreach (var key in fonts.Elements.Keys)
            {
                var font = fonts.Elements.GetDictionary(key)!;
                var descendant = font.Elements.GetArray("/DescendantFonts")!.Elements.GetDictionary(0)!;
                var embedded = descendant.Elements.GetDictionary("/FontDescriptor")!.Elements.GetDictionary("/FontFile2")!.Stream.UnfilteredValue;
                using var data = global::SkiaSharp.SKData.CreateCopy(embedded);
                using var actual = global::SkiaSharp.SKTypeface.FromData(data);
                Assert.Equal(expected.FamilyName, actual.FamilyName);
                using var e = new global::SkiaSharp.SKFont(expected, 1000) { LinearMetrics = true, Subpixel = true };
                using var a = new global::SkiaSharp.SKFont(actual, 1000) { LinearMetrics = true, Subpixel = true };
                var glyphs = e.GetGlyphs("共通 ABC");
                Assert.Equal(glyphs, a.GetGlyphs("共通 ABC"));
                Assert.Equal(e.GetGlyphWidths(glyphs), a.GetGlyphWidths(glyphs));
                foreach (var glyph in glyphs)
                {
                    using var ep = e.GetGlyphPath(glyph);
                    using var ap = a.GetGlyphPath(glyph);
                    Assert.Equal(ep?.ToSvgPathData(), ap?.ToSvgPathData());
                }
            }
        }
    }

    private static Task Report(string id, byte[] bytes, string status, string? error) => File.WriteAllTextAsync(Path.Combine(EvidenceDirectory, id + ".json"), JsonSerializer.Serialize(new { id, status, error, input_sha256 = Convert.ToHexString(SHA256.HashData(bytes)), font_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FontPath))), tolerance_pt = 0.01 }, new JsonSerializerOptions { WriteIndented = true }));
}

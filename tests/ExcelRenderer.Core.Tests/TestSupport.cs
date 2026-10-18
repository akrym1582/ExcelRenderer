using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.TestFixtures;
using S = DocumentFormat.OpenXml.Spreadsheet;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Input;
using ExcelRenderer.Core.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Core.Tests;

internal static class TestSupport
{
    internal static string JapaneseFont => Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf");
    internal static string MonoFont => Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSansMono-Regular.ttf");
    internal static PdfExportOptions Options => new() { FontFilePath = JapaneseFont };

    internal static MemoryStream Workbook(Action<WorkbookPart>? configure = null)
    {
        var stream = SdkWorkbook.Create(new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "帳票 日本語 abc 123")) { RowIndex = 1 })), SdkWorkbook.Styles(), name: "First");
        if (configure is not null)
        {
            using var document = SpreadsheetDocument.Open(stream, true);
            configure(document.WorkbookPart!);
        }
        stream.Position = 0;
        return stream;
    }

    internal static S.Worksheet Sheet(WorkbookPart workbook) => workbook.WorksheetParts.First().Worksheet;

    internal static void Print(WorkbookPart workbook, string reference, string name = "_xlnm.Print_Area")
    {
        workbook.Workbook.DefinedNames ??= new S.DefinedNames();
        workbook.Workbook.DefinedNames.Append(new S.DefinedName(reference) { Name = name, LocalSheetId = 0 });
    }

    internal static void MergeDoubleBorder(WorkbookPart workbook)
    {
        var sheet = Sheet(workbook);
        sheet.Append(new S.MergeCells(new S.MergeCell { Reference = "A1:B2" }));
        var data = sheet.GetFirstChild<S.SheetData>()!;
        var first = data.Elements<S.Row>().Single();
        first.Append(new S.Cell { CellReference = "B1" });
        data.Append(new S.Row(new S.Cell { CellReference = "A2" }, new S.Cell { CellReference = "B2" }) { RowIndex = 2 });
        var styles = workbook.WorkbookStylesPart!.Stylesheet;
        foreach (var cell in data.Descendants<S.Cell>())
        {
            var address = cell.CellReference!.Value!;
            var border = new S.Border();
            if (address.StartsWith("A")) border.LeftBorder = new S.LeftBorder { Style = S.BorderStyleValues.Double };
            if (address.EndsWith("1")) border.TopBorder = new S.TopBorder { Style = S.BorderStyleValues.Double };
            if (address.StartsWith("B")) border.RightBorder = new S.RightBorder { Style = S.BorderStyleValues.Double };
            if (address.EndsWith("2")) border.BottomBorder = new S.BottomBorder { Style = S.BorderStyleValues.Double };
            var format = (S.CellFormat)styles.CellFormats!.FirstChild!.CloneNode(true);
            format.BorderId = (uint)styles.Borders!.ChildElements.Count;
            styles.Borders.Append(border);
            cell.StyleIndex = (uint)styles.CellFormats.ChildElements.Count;
            styles.CellFormats.Append(format);
        }
    }

    internal static async Task<(ConversionResult Result, byte[] Pdf)> Convert(Action<WorkbookPart>? configure = null, PdfExportOptions? options = null)
    {
        using var input = Workbook(configure);
        using var output = new MemoryStream();
        var result = await ExcelConverter.ConvertAsync(input, output, options ?? Options);
        return (result, output.ToArray());
    }

    internal static PdfTestFile Open(byte[] bytes) => PdfTestFile.Open(new MemoryStream(bytes));

    internal static IEnumerable<PdfTestMap> Fonts(PdfTestPage page)
    {
        var fonts = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font");
        return fonts?.Elements.Keys.Select(key => fonts.Elements.GetDictionary(key)!) ?? [];
    }

    internal static PdfTestMap Descriptor(PdfTestMap font) =>
        ((PdfTestMap)((PdfTestReference)font.Elements.GetArray("/DescendantFonts")!.Elements[0]).Value).Elements.GetDictionary("/FontDescriptor")!;

    internal static string FontName(byte[] bytes)
    {
        using var document = Open(bytes);
        return Descriptor(Fonts(document.Pages[0]).First()).Elements.GetName("/FontName").Replace(" ", string.Empty);
    }

    internal static async Task WithSheet(Stream input, Action<ReportSheet, SingleFontContext> action)
    {
        using var prepared = await WorkbookInputPreparer.ReadAsync(input, new(), default);
        using var font = new SingleFontContext(JapaneseFont);
        action(new CoreExcelReader(font).Read(prepared, null).Sheets.Single(), font);
    }

    internal static byte[] Png(SKColor? color = null)
    {
        using var bitmap = new SKBitmap(3, 3);
        bitmap.Erase(color ?? SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    internal static void Near(double expected, double actual, double tolerance = 0.02) =>
        Xunit.Assert.InRange(actual, expected - tolerance, expected + tolerance);
}

internal sealed class TestStream(Stream source, bool readable = true, bool writable = true, bool seekable = true, Action? onRead = null, Action? onWrite = null) : Stream
{
    internal bool Disposed { get; private set; }
    public override bool CanRead => readable;
    public override bool CanWrite => writable;
    public override bool CanSeek => seekable;
    public override long Length => source.Length;
    public override long Position { get => source.Position; set => source.Position = value; }
    public override void Flush() => source.Flush();
    public override int Read(byte[] buffer, int offset, int count) { onRead?.Invoke(); return source.Read(buffer, offset, count); }
    public override void Write(byte[] buffer, int offset, int count) { onWrite?.Invoke(); source.Write(buffer, offset, count); }
    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);
    public override void SetLength(long value) => source.SetLength(value);
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

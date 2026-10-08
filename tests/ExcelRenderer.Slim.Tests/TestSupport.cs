using ClosedXML.Excel;
using ExcelRenderer.Slim.Excel;
using ExcelRenderer.Slim.Fonts;
using ExcelRenderer.Slim.Model;
using ExcelRenderer.Slim.Input;
using ExcelRenderer.Slim.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace ExcelRenderer.Slim.Tests;

internal static class TestSupport
{
    internal static string JapaneseFont => Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf");
    internal static string MonoFont => Path.Combine(AppContext.BaseDirectory, "Fonts", "NotoSansMono-Regular.ttf");
    internal static SlimPdfOptions Options => new() { FontFilePath = JapaneseFont };

    internal static MemoryStream Workbook(Action<XLWorkbook>? configure = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("First");
        sheet.Cell(1, 1).Value = "帳票 日本語 abc 123";
        configure?.Invoke(workbook);
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    internal static async Task<(SlimPdfResult Result, byte[] Pdf)> Convert(Action<XLWorkbook>? configure = null, SlimPdfOptions? options = null)
    {
        using var input = Workbook(configure);
        using var output = new MemoryStream();
        var result = await SlimExcelConverter.ConvertAsync(input, output, options ?? Options);
        return (result, output.ToArray());
    }

    internal static PdfDocument Open(byte[] bytes) => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

    internal static IEnumerable<PdfDictionary> Fonts(PdfPage page)
    {
        var fonts = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font");
        return fonts?.Elements.Keys.Select(key => fonts.Elements.GetDictionary(key)!) ?? [];
    }

    internal static PdfDictionary Descriptor(PdfDictionary font) =>
        ((PdfDictionary)((PdfReference)font.Elements.GetArray("/DescendantFonts")!.Elements[0]).Value).Elements.GetDictionary("/FontDescriptor")!;

    internal static string FontName(byte[] bytes)
    {
        using var document = Open(bytes);
        return Descriptor(Fonts(document.Pages[0]).First()).Elements.GetName("/FontName").Replace(" ", string.Empty);
    }

    internal static async Task WithSheet(Stream input, Action<ReportSheet, SingleFontContext> action)
    {
        using var prepared = await WorkbookInputPreparer.ReadAsync(input, new(), default);
        using var font = new SingleFontContext(JapaneseFont);
        GlobalFontSettings.ResetFontManagement();
        GlobalFontSettings.FontResolver = font.Resolver;
        try { action(new ExcelReader(font).Read(prepared, null).Sheets.Single(), font); }
        finally { GlobalFontSettings.ResetFontManagement(); }
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

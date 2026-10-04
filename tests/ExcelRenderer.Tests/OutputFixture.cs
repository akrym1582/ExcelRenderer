using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace ExcelRenderer.Tests;

internal static class OutputFixture
{
    internal static readonly PageSettings Page = new(120, 80);

    internal static ResolvedFont Face => new("Noto Sans JP", 400, false, string.Empty)
    {
        FaceId = "output-fixture-noto",
        FontData = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf")),
    };

    internal static SKTypeface Typeface(bool mono = false) => SKTypeface.FromFile(Path.Combine(
        AppContext.BaseDirectory, mono ? "Fonts/NotoSansMono-Regular.ttf" : "NotoSansJP-Regular.ttf"));

    internal static DrawTextCommand Artificial(bool runs = true, bool underline = false,
        HorizontalAlignment horizontal = HorizontalAlignment.Left, VerticalAlignment vertical = VerticalAlignment.Top)
    {
        var face = Face;
        return new(1, new(10, 20, 100, 50), "ABC", CellStyle.Default with
        {
            Font = new("Noto Sans JP", 12, Underline: underline),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
        })
        {
            TextLayout = new(new(40, 30),
            [
                new("AB", 40, 10, 3, runs ? [new(new("A", face), 0, 5), new(new("B", face), 25, 5)] : [], false),
                new("C", 20, 20, 17, runs ? [new(new("C", face), 7, 5)] : [], false),
            ]) { EffectiveFontSize = 9 },
        };
    }

    internal static byte[] Pdf(DrawTextCommand command)
    {
        using var output = new MemoryStream();
        new PdfSharpRenderer().Render([command], Page, output);
        return output.ToArray();
    }

    internal static string Content(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        return string.Join("\n", ContentReader.ReadContent(document.Pages[0]).Cast<global::PdfSharp.Pdf.Content.Objects.COperator>().Select(op => string.Join(" ", op.Operands.Cast<global::PdfSharp.Pdf.Content.Objects.CObject>().Select(x => x.ToString())) + " " + op.OpCode.Name));
    }

    internal sealed class FixedManager : IFontManager
    {
        public ResolvedFont Resolve(FontRequest request) => Face;
        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request) => [new(text, Face)];
    }
}

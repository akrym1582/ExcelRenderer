using UglyToad.PdfPig;

namespace ExcelRenderer.TestFixtures;

internal static class PdfText
{
    internal static string Read(byte[] bytes)
    {
        using var document = PdfDocument.Open(bytes);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }
}

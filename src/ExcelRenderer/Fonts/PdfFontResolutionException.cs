namespace ExcelRenderer.Fonts;

/// <summary>Retains the rejection details when every matching PDF font is unusable.</summary>
internal sealed class PdfFontResolutionException(string message) : InvalidOperationException(message);

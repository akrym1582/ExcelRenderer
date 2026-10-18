namespace ExcelRenderer;

/// <summary>Selects PDF drawing. Both backends currently retain PDFsharp measurement and dependencies.</summary>
public enum PdfBackend
{
    /// <summary>Uses the existing PDFsharp renderer.</summary>
    PdfSharp = 0,

    /// <summary>Uses SkiaSharp with subset fonts and structural compatibility processing.</summary>
    SkiaSharp = 1,
}

namespace ExcelRenderer;

/// <summary>Identifies the PDF engine. Only SkiaSharp is supported.</summary>
public enum PdfBackend
{
    /// <summary>Reserved obsolete value. Explicit selection throws before reading input or creating output.</summary>
    [Obsolete("PDFsharp has been removed. Specify SkiaSharp.")]
    PdfSharp = 0,

    /// <summary>Uses SkiaSharp with shared subset fonts and preserved source Unicode.</summary>
    SkiaSharp = 1,
}

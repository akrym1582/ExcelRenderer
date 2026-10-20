namespace ExcelRenderer.Core.Rendering;

/// <summary>Describes validated raster alpha without retaining a bitmap.</summary>
internal enum ImageAlphaKind
{
    /// <summary>Every pixel is fully opaque.</summary>
    Opaque,

    /// <summary>Every pixel is fully transparent.</summary>
    Transparent,

    /// <summary>Opaque and transparent pixels form a binary mask.</summary>
    BinaryMask,

    /// <summary>At least one pixel has partial alpha.</summary>
    Partial,
}

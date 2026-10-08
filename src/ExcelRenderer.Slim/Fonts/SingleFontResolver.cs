using PdfSharp.Fonts;

namespace ExcelRenderer.Slim.Fonts;

/// <summary>Resolves every family request to one regular face without simulation.</summary>
internal sealed class SingleFontResolver : IFontResolver
{
    private readonly byte[] bytes;

    /// <summary>Initializes a new instance of the <see cref="SingleFontResolver"/> class.</summary>
    /// <param name="bytes">The immutable font snapshot.</param>
    internal SingleFontResolver(byte[] bytes) => this.bytes = bytes;

    /// <inheritdoc/>
    public byte[] GetFont(string faceName) => bytes;

    /// <inheritdoc/>
    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(SingleFontContext.Family);
}

using SkiaSharp;

namespace ExcelRenderer.Fonts;

/// <summary>Disposes a temporary face while leaving a conversion-owned face alive.</summary>
internal readonly struct TypefaceLease : IDisposable
{
    private readonly bool _ownsTypeface;

    /// <summary>Initializes a new instance of the <see cref="TypefaceLease"/> struct.</summary>
    /// <param name="font">The resolved physical face.</param>
    internal TypefaceLease(ResolvedFont font)
    {
        if (ConversionFontResources.Current is { } resources)
        {
            Typeface = resources.GetTypeface(font);
            _ownsTypeface = false;
        }
        else
        {
            using Stream stream = font.FontData is null ? File.OpenRead(font.FilePath) : new MemoryStream(font.FontData, false);
            Typeface = SKTypeface.FromStream(stream);
            _ownsTypeface = true;
        }
    }

    /// <summary>Initializes a new instance of the <see cref="TypefaceLease"/> struct.</summary>
    /// <param name="typeface">An owned system-selected face, or null.</param>
    internal TypefaceLease(SKTypeface? typeface)
    {
        Typeface = typeface;
        _ownsTypeface = true;
    }

    /// <summary>Gets the borrowed or owned face.</summary>
    internal SKTypeface? Typeface { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsTypeface)
        {
            Typeface?.Dispose();
        }
    }
}

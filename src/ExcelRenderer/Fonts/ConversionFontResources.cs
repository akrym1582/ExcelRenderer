using System.Runtime.CompilerServices;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Rendering;
using SkiaSharp;

namespace ExcelRenderer.Fonts;

/// <summary>Owns immutable physical font snapshots only for the current conversion.</summary>
internal sealed class ConversionFontResources : IDisposable
{
    private static readonly AsyncLocal<ConversionFontResources?> CurrentSlot = new();
    private readonly ConversionFontResources? _previous;
    private readonly Dictionary<string, SkiaFontFace> _snapshots = new(StringComparer.Ordinal);
    private ConditionalWeakTable<ResolvedFont, SkiaFontFace> _faces = new();
    private bool _disposed;
    private bool _purgeFontCache;

    /// <summary>Initializes a new instance of the <see cref="ConversionFontResources"/> class.</summary>
    internal ConversionFontResources()
    {
        _previous = CurrentSlot.Value;
        CurrentSlot.Value = this;
    }

    /// <summary>Gets the resource owner for the current conversion, if any.</summary>
    internal static ConversionFontResources? Current => CurrentSlot.Value;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CurrentSlot.Value = _previous;
        foreach (var face in _snapshots.Values)
        {
            face.Dispose();
        }

        if (_snapshots.Count > 0 || _purgeFontCache)
        {
            // Skia's process-wide strike cache can retain a disposed face's font data.
            // Purge unused strikes after our SKFont instances and typefaces are released;
            // strikes referenced by active drawing operations remain valid.
            SKGraphics.PurgeFontCache();
        }

        _faces = new();
        _snapshots.Clear();
    }

    /// <summary>Requests one cache purge after all conversion-owned drawing faces are released.</summary>
    internal void RequestCachePurge() => _purgeFontCache = true;

    /// <summary>Gets one immutable physical snapshot owned by this scope.</summary>
    /// <param name="font">The resolved physical font.</param>
    /// <returns>The scope-owned snapshot.</returns>
    internal SkiaFontFace GetFace(ResolvedFont font)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConversionFontResources));
        }

        if (!_faces.TryGetValue(font, out var face))
        {
            var bytes = font.SnapshotData ?? font.FontData ?? File.ReadAllBytes(font.FilePath);
            var identity = SkiaFontFace.ContentIdentity(bytes);
            if (!_snapshots.TryGetValue(identity, out face))
            {
                face = new SkiaFontFace(bytes, identity);
                _snapshots.Add(identity, face);
            }

            _faces.Add(font, face);
        }

        return face;
    }

    /// <summary>Pins a resolved font to this scope's immutable bytes for replay after disposal.</summary>
    /// <param name="font">The resolved font.</param>
    /// <returns>A handle-free snapshot-backed font identity.</returns>
    internal ResolvedFont Freeze(ResolvedFont font)
    {
        var face = GetFace(font);
        var frozen = font with { SnapshotData = face.Data };
        _faces.Add(frozen, face);
        return frozen;
    }

    /// <summary>Gets the borrowed native face.</summary>
    /// <param name="font">The physical font.</param>
    /// <returns>The scope-owned typeface.</returns>
    internal SKTypeface GetTypeface(ResolvedFont font) => GetFace(font).Typeface;

    /// <summary>Gets the shared line metrics.</summary>
    /// <param name="font">The physical font.</param>
    /// <param name="size">The point size.</param>
    /// <returns>The point metrics.</returns>
    internal (double Ascent, double Descent, double Leading) GetMetrics(ResolvedFont font, double size) => GetFace(font).Metrics(size);
}

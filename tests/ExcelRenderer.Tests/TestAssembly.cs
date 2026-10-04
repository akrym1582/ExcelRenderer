using System.Runtime.CompilerServices;
using ExcelRenderer.Fonts;
using ExcelRenderer.PdfSharp;
using PdfSharp.Fonts;
using SkiaSharp;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ExcelRenderer.Tests;

/// <summary>Configures process-wide dependencies before any tests use PDFsharp.</summary>
internal static class TestAssembly
{
    /// <summary>Installs the deterministic test font resolver before PDFsharp initializes its font cache.</summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Each saved-workbook test creates resolved faces. Bound Skia's retained native
        // strike cache so the full suite fits the same memory budget as isolated tests.
        SKGraphics.SetFontCacheLimit(16 * 1024 * 1024);
        SKGraphics.SetFontCacheCountLimit(32);
        GlobalFontSettings.ResetFontManagement();
        GlobalFontSettings.FontResolver = new PdfSharpFontResolver(new FontManager(new FontOptions
        {
            AllowSystemFonts = false,
        }));
    }
}

using ExcelRenderer.TestFixtures;
using System.Runtime.CompilerServices;
using ExcelRenderer.Fonts;
using ExcelRenderer.Pdf;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ExcelRenderer.Tests;

/// <summary>テストの Skia キャッシュ上限を設定します。</summary>
internal static class TestAssembly
{
    /// <summary>Skia のフォントキャッシュを 16 MiB・32 項目に制限します。</summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Each saved-workbook test creates resolved faces. Bound Skia's retained native
        // strike cache so the full suite fits the same memory budget as isolated tests.
        SKGraphics.SetFontCacheLimit(16 * 1024 * 1024);
        SKGraphics.SetFontCacheCountLimit(32);
    }
}

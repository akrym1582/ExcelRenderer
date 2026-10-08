using System.Runtime.CompilerServices;
using ExcelRenderer.Fonts;
using ExcelRenderer.PdfSharp;
using PdfSharp.Fonts;
using SkiaSharp;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ExcelRenderer.Tests;

/// <summary>PDFsharp の初期化前にテスト用のフォントリゾルバーと Skia のキャッシュ上限を設定します。</summary>
internal static class TestAssembly
{
    /// <summary>Skia のフォントキャッシュを 16 MiB・32 項目に制限し、PDFsharp に同梱フォントを使用するテスト用リゾルバーを登録します。</summary>
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

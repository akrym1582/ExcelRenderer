using System.Runtime.CompilerServices;
using ExcelRenderer.Fonts;
using ExcelRenderer.PdfSharp;
using PdfSharp.Fonts;
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
        GlobalFontSettings.ResetFontManagement();
        GlobalFontSettings.FontResolver = new PdfSharpFontResolver(new FontManager(new FontOptions
        {
            AllowSystemFonts = false,
        }));
    }
}

# Core explicit font selection

[日本語](core-fonts.ja.md)

Core's API accepts either the existing `PdfExportOptions.FontFilePath` and `FontCollectionIndex`, or the new `Fonts` configuration. Combining them is an argument error. The single-font API retains its existing behavior.

```csharp
var options = new ExcelRenderer.Core.PdfExportOptions
{
    Fonts = new()
    {
        Files = ["NotoSansJP-Regular.ttf", "NotoSerifJP-Regular.ttf"],
        FamilyAliases =
        {
            ["游ゴシック"] = "Noto Sans JP",
            ["游明朝"] = "Noto Serif JP",
        },
        SansSerifFallback = "Noto Sans JP",
        SerifFallback = "Noto Serif JP",
        DefaultFamily = "Noto Sans JP",
    },
};
await ExcelRenderer.Core.ExcelConverter.ConvertAsync(xlsx, pdf, options);
```

`Fonts` has type `ExcelRenderer.Core.Fonts.FontOptions`. Supply at least one static TrueType file; each collection file uses face zero. Existing PDF font-format validation applies to every file. Files are read into conversion-owned snapshots, used consistently for measurement, wrapping, shrinking, glyph selection, and PDF embedding. Duplicate physical faces share PDF subsets. No new font package dependency is added to Core.

Each cell selects one face in this order:

1. A supplied file declaring the requested family (including localized family names).
2. A direct `FamilyAliases` entry mapping that name to a supplied family.
3. `SerifFallback` for saved SpreadsheetML font `<family val="1"/>` (Roman), or `SansSerifFallback` for `<family val="2"/>` (Swiss).
4. `DefaultFamily`, or the first file if that setting is omitted.

Family names and aliases are matched without case sensitivity. File order breaks ties for matching faces; no bold/italic face selection or synthesis is added. Alias targets and configured fallback/default families must exist in `Files`; unknown targets fail rather than silently choosing another family. Alias chains are not followed. An exact physical-family match takes priority over an alias.

Classification comes from saved workbook font metadata, not guessed from the font name. Missing/other classifications, or an omitted classified fallback, use the default. Specify aliases when the workbook does not reliably distinguish Gothic/Mincho families. Headers and footers use the default drawing font; their existing formatting-code handling is unchanged.

Core still removes variation selectors and ignores partial rich-text font formatting and cell bold/italic. A whole cell uses one selected font: an unsupported character is a missing glyph in that font, even if another supplied font contains it. There is no OS discovery for drawing fonts, automatic per-character fallback, or shaping extension.

Column metrics remain independent. Exact original Normal families are measured from any supplied file. Aliases/classified fallbacks are substitutes, and do not count as original-family matches. An unavailable source or `UseSourceFontForColumnWidths = false` uses the existing fixed 8px fallback by default. Set `ReadOptions.FallbackMaximumDigitWidthPixels96Dpi = null` to measure the selected Normal drawing substitute instead, or supply a numeric width. Explicit `MaximumDigitWidthPixels96Dpi` still wins. Optional source-file/system discovery remains limited to column measurements.

## CLI default

```bash
excelrenderer core input.xlsx -o output.pdf
excelrenderer core input.xlsx -o custom.pdf --font-file ./regular.ttf
```

With no `--font-file`, the Tool resolves Noto Sans JP from its existing font pack, passes a temporary font snapshot to Core, and removes the temporary file on success or failure. It does not search OS fonts for this drawing default. Missing bundled fonts cause an error; supply `--font-file` in that case. `--font-collection-index` requires an explicit file for nonzero values. CLI drawing remains single-font; the multiple-file and alias configuration above is an API feature. Core API callers must still explicitly supply fonts.

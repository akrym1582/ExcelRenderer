# Managed TrueType PDF fonts

Core implements the font subset and TTC/OTC extraction with BCL-only C# under
`Fonts/TrueType`. Main, Core and the CLI use the same subsetter. Rendering and
PDF generation still require SkiaSharp 4.151.0 and its native assets. Normal
restore/build/test/pack requires no custom native subset adapter or source download.

| Selected input face | PDF behavior |
| --- | --- |
| Static sfnt 0x00010000 with glyf/loca | Managed subset, retaining original GIDs |
| Proportional/monospaced, separate regular/bold/italic files | Same supported static TrueType route |
| Static TrueType face in TTC v1/v2 or OTC | Extract selected face, then subset |
| Existing nonvariable COLR/CPAL v0/v1, CBDT/CBLC, sbix | Original normalized face through existing Skia color route |
| CFF/CFF2, OTTO, variable fonts | Rejected for PDF, including color-table combinations |
| WOFF/WOFF2, Type1, legacy true/typ1 | Unsupported |
| Broken directories, glyphs or references | Table-specific invalid-data diagnostic |

This preserves the existing color engine, including Noto Color Emoji, without
adding guarantees for every bitmap/SVG format or new emoji sequence. Color bytes
and paint/layer GIDs are preserved; the outline subsetter never processes them.
Skia may emit images/Type3 color glyphs. Color output has no outline subset size
promise. Restricted embedding is rejected on both routes; no-subsetting and
bitmap-only flags reject outline subsetting without clearing the license bits.

PDF restrictions apply when collecting/preparing fonts actually used on selected
pages. Merely registering an unsupported face does not fail conversion. Shared
font resolution/PNG/SVG/Markdown do not inherit the PDF-only restrictions. Core
retains its single supplied regular font and variation-selector-to-base policy.
Main retains selected IVS GIDs and original Unicode/cluster data for search/copy.
IVS and cmap format 14 do not indicate a variable font; STAT alone is also allowed.

## Selecting a collection face

Indices are zero-based. Standalone fonts accept only zero. Existing positional
record constructors and Register(string, ...) signatures are unchanged.

```csharp
var path = "/fonts/family.ttc";
var fonts = new ExcelRenderer.Fonts.FontOptions
{
    AllowSystemFonts = false,
    Registrations = new[]
    {
        new ExcelRenderer.Fonts.FontRegistration("My Family", path, path)
        {
            RegularCollectionIndex = 0,
            BoldCollectionIndex = 1,
            // ItalicCollectionIndex / BoldItalicCollectionIndex are also available.
        },
    },
};
// Or: manager.Register(new FontRegistration(...) { ... });

var core = new ExcelRenderer.Core.PdfExportOptions
{
    FontFilePath = path,
    FontCollectionIndex = 1,
};

// External IFontManager implementations can supply file or memory containers:
var memoryFace = new ExcelRenderer.Fonts.ResolvedFont("My Family", 700, false, "")
{
    FontData = System.IO.File.ReadAllBytes(path),
    FontCollectionIndex = 1,
};
```

`FontOptions.FontFiles` and directory/system scans enumerate all collection faces
using each face's actual family/weight/italic attributes. Ties use the existing
priority/order then path/index. Metadata scanning does not copy all face tables.
Selected snapshots are normalized lazily. File snapshot keys include path,
length, mtime and index; container bytes are shared within the manager/conversion.
Physical subset sharing uses normalized content hashes, never path alone.
Frozen layouts distinguish normalized bytes from caller-supplied containers.

To choose one face from exactly one direct file, set
`FontOptions.FontFileCollectionIndex = 1`. Null (the default) enumerates all faces.
An explicit index with zero/multiple files or a negative index is an input error.

```bash
excelrenderer pdf report.xlsx -o report.pdf \
  --font-file /fonts/family.ttc --font-face-index 1 \
  --fallback-font "My Family" --no-system-fonts
excelrenderer render report.xlsx --format pdf -o report.pdf \
  --font-file /fonts/family.ttc --font-face-index 1 --fallback-font "My Family"
```

The CLI index requires exactly one `--font-file`. Without an index all faces are
candidates. The selected font must match a requested/fallback family; supplying
an index does not change workbook font-family selection rules.

## Internal subset contract

Selection, IVS resolution, advances, line breaking, positions and page planning
use the original immutable normalized face. PDF collection includes GID 0 and
all transitive composite components. An iterative DFS detects cycles while
allowing shared components. Simple/composite structures and instructions are
bounded before copying. Cancellation remains OperationCanceledException.

The subset retains maxp.numGlyphs and all horizontal/vertical metrics. Unused
outlines become equal adjacent loca offsets. Retained outlines/instructions and
component IDs remain intact. The writer emits long loca, aligns glyphs to two
bytes and tables to four, computes table checksums and head.checkSumAdjustment,
and derives head bounds from retained glyph bounds. Timestamps are unchanged.

Long loca input offsets may be odd: the committed Noto Sans JP and IPAmj fonts
use packed odd offsets. Rejecting them would break required font support. Range
and monotonicity checks still apply, and output adds zero alignment padding.
Short loca intrinsically uses two-byte units. This is an evidence-based
adjustment to the original plan's blanket even-input-offset requirement.

The PDF-only allowlist retains head/maxp/hhea/hmtx/cmap/name/OS/2/post,
vhea/vmtx when paired, and cvt/fpgm/prep/gasp when present. Layout tables,
DSIG and other tables are omitted. Cmap (including IVS) remains unchanged and can
map unused characters to empty outlines. Never resolve new characters or shape
text from this internal subset. No installable-font API is exposed.

TTC extraction is separate: it preserves every selected table except invalidated
DSIG, uses container-absolute table offsets and permits tables shared across
faces. Each selected face is normalized before selection, measurement and drawing.
Its outline/color/layout data are preserved. No PDF-only format gate is applied
by the shared extractor. Unknown table payloads are opaque; no new support for
special font technologies requiring relocation of container-absolute internal
references is claimed.

Subset failures do not switch to full embedding, another font, another PDF
engine or rasterized pages. The internal fullEmbed switch exists only for
comparison. A font-preparation failure does not commit a completed PDF.
Core retains its historical InvalidDataException surface for unsupported inputs;
main reports a conversion diagnostic with the underlying format reason. Explicit
FontFiles validation retains ArgumentException. Index errors are argument errors.

## Verification

See [recorded results](validation/managed-truetype-subset/README.md). Reproduce:

```bash
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh # Linux deterministic Fontconfig setup
dotnet test ExcelRenderer.slnx -c Release --no-build --no-restore -m:1
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -m:1 -o artifacts/packages
python3 scripts/package_distribution.py --packages artifacts/packages
python3 scripts/validate-formulas-packages.py --packages artifacts/packages --work /tmp/excelrenderer-consumers
```

Independent fontTools/Poppler/MuPDF tools and fixture generators are validation
only. Product runtime and ordinary subset build/pack do not invoke them.

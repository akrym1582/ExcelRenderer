# PDFsharp removal inventory

Baseline: `71deba48ea4a397641c9ad155bc1c8b3ddf8b5d9` (draft PR #89).
Main at start: `8d23c0725f60126f5e38c9a9e9a612a6d276b93a`; no conflicting main updates.
Work branch: `feature/remove-pdfsharp`. No publication or release is authorized by the removal plan.

| Owner / previous implementation | Responsibility | Disposition |
| --- | --- | --- |
| main PdfSharpTextMeasurer / RichTextLayoutKey | Wrap, runs, metrics, caches | Neutral layout helpers in Core; public SkiaTextMeasurer in main |
| main PdfSharpRenderer / text painters / FullPdfRenderer | Drawing / full differences | Delete; shared Skia primitives in Core with full adapters |
| main PdfSharpFontResolver | Global resolver / registrations | Delete; keep physical FontManager resolution |
| ConversionFontResources / RenderResourceSession | Native fonts plus XFont / lease | Remove XFont / lease; conversion-owned immutable snapshots |
| ExcelConverter / DrawCommandBounds | Defaults, gate, fallback | Skia layout / scope; no global serialization |
| Core SingleFontContext / resolver / measurer | Static TrueType / metrics / MDW | Snapshot and shared metrics; delete resolver |
| Core CorePdfRenderer / text painter / extension contract | PDF lifecycle / basic drawing | Skia lifecycle, subset, cell drawing owned by Core |
| Core PdfSharpFontGate | Process-wide serialization | Delete after all global font state is gone |
| SkiaPdfCompatibilityFinalizer | Page boxes, tooltips, image interpolation, implicit RGB transparency group | Bounded parser + incremental update for generated Skia PDFs |
| Project / lock / packaging | Runtime dependency graph | Remove PDFsharp and proven-unused Pkcs reference; restore and audit |
| PDF probes / validation tool | Test-only reader / operators | Neutral probes plus independent MuPDF / Poppler integration |
| Mutation / dependency scripts | Old type and file locations | Port mutations to equivalent new implementations |
| A/B/Core PDFs and recorded validation | Historical compatibility evidence | Keep frozen outputs; never rebuild from old code in normal CI |

The old finalizer also sets `/Interpolate true` on non-mask images. The old save path additionally creates a page `/Group << /CS /DeviceRGB /S /Transparency >>`. Its absence changes raster AA/compositing despite byte-identical content and font streams; preserve the group explicitly. Keep the established 0.001pt box serialization precision (maximum error 0.0005pt). Image stream bytes must be retained exactly. Core's regular static TrueType validation, variation-selector reduction, stream ownership and rounded 96-dpi MDW remain product constraints.

PDFsharp 6.2.3 introduced System.Security.Cryptography.Pkcs. Source search finds no product use of Pkcs types; remove its explicit pin together with that dependency, then verify the restored graph.

Public breaking changes are tracked in `pdfsharp-migration.md`. Completion evidence belongs in `pdfsharp-removal-validation.md`; this inventory is not a claim of completion.

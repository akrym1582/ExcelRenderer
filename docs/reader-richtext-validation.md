# Reader and partial-text implementation status

This work is **not acceptance-complete for the four-item request**. Recalculate is blocked by the required evaluation scope in ClosedXML 0.105.1.
Legacy and CachedValue are implemented through Reader, API, CLI and all output paths.
Recalculate is reserved but currently fails before conversion; it is not an implemented
evaluation mode. No broader evaluation scope or always-cache fallback has been adopted.

## Baseline and environment

- Repository: `akrym1582/ExcelRenderer`.
- Fetched `origin/main`: `06280e538df4ebc49989efcb3f239379ffa1e09b`.
- Original local branch: `work`, clean, identical to fetched main (0/0 divergence).
- Independent baseline worktree: `/tmp/excel-baseline`, detached at that SHA.
- Debian 13 x64, .NET SDK 10.0.401, runtime 10.0.12.
- Implementation commit: `232c4cd6a7277c2824593dd0ceded8c8caa7a7d9`.
- Follow-up: Pkcs and its ASN.1 dependency were patched from 8.0.1 to 8.0.2;
  ClosedXML 0.105.1 and PDFsharp 6.2.3 remain fixed.

The exact requested `dotnet restore ExcelRenderer.slnx` failed on the unmodified
baseline: NuGet audit reports NU1902/NU1903 for transitive
`System.Security.Cryptography.Pkcs 8.0.1` (GHSA-2fmv-rv2c-59x7 and
GHSA-846f-f764-76mx). Baseline build/test were retried after
`dotnet restore ExcelRenderer.slnx -p:NuGetAudit=false`, followed by `--no-restore`.
This is a validation-only command override, not a project or analyzer suppression.
It does not count as a successful run of the original restore command.

The independent baseline rebuild had zero warnings/errors. Baseline regression
results: main 410, Core 76, Mapping 49, Tool 51 passed; zero skipped/failed.

## Formula evaluation blocker

The pinned package identifies upstream commit
`b4ebe47bd3ecebf8480dea7422188d16b35b9d9a`.
[XLCell.Evaluate in 0.105.1](https://github.com/ClosedXML/ClosedXML/blob/0.105.1/ClosedXML/Excel/Cells/XLCell.cs)
invokes `wb.CalcEngine.Recalculate(wb, null)` when a dirty cell's Value is read.
Worksheet.Evaluate uses recursive evaluation, whose dirty dependencies read cell.Value.
Thus it can reach an unrelated unselected sheet's circular formula.

`FormulaEvaluationScopeTests.ClosedXmlDirtyDependencyEvaluationCanReachUnrelatedSheet` reproduces
that behavior with a selected sheet containing an ordinary dependency and an
unselected circular cell. The public-API-only, fixed-package requirement therefore
needs an agreed evaluation-scope design before the requested Recalculate contract
can be claimed. No private reflection, custom formula interpreter, unconditional
fallback implementation, or silent whole-workbook substitution was introduced.
CachedValue and its complete API/CLI propagation are now implemented and tested.
The formula-mode acceptance group remains incomplete specifically for Recalculate.

## Implemented paths and limitations

- One lazy scratch workbook per Core read; deterministic counters report zero or one.
  The workbook date system is copied, the scratch cell is cleared between uses, and
  using-scoped disposal covers exceptions/cancellation.
- Workbook-local source-style value equality cache, capped at 8192 entries with hits
  still available after capacity. The read path does not mutate source styles.
  Tests verify public ClosedXML Equals/GetHashCode and scratch copy independence.
  General alignment is distinguished by source cached value type.
- Public partial-text models, typed Core payloads, reader projection, immutable manual
  run snapshots, optional layout capability, Unicode element wrapping, mixed baseline
  metrics, structural bounded cache, shrink/print/payload scale, PDF/Skia run color,
  size and underline, and trim ink bounds.
- Opt-in cell clipping/small-size diagnostics in selected-page preflight. Logical
  clipping belongs to Core; this is a 0.25pt logical metric check, not exhaustive ink
  bounds. Hidden cells and explicit viewport clips do not generate clipping warnings.
- Opt-in conditional-format candidates, partial-format warnings, and deferred anchored
  chart diagnostics using the existing `UnsupportedDrawingObject` code. Existing
  unsupported drawing diagnostics retain their legacy behavior when opt-in is off.
- Formula results remain plain strings; Markdown emits plain text; Core alone retains
  its regular single-font and base-character-only behavior.

Chart-sheet diagnostics and legacy-measurer fallback diagnostics are now implemented.
The follow-up test matrix covers shared/inline text, inheritance/theme tint, rotation,
CRLF and variation-selector preservation, merged shrink, repeated titles, and range/trim
in PDF/PNG/SVG. Recalculate failure/result/cache-fallback acceptance remains blocked by
the unresolved scope contract. Excel application parity is intentionally unverified.

## Reproduction

```sh
dotnet restore ExcelRenderer.slnx
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal
dotnet test ExcelRenderer.slnx --no-restore
python3 scripts/check-core-dependencies.py
dotnet run --project tools/ExcelRenderer.Performance -c Release -- reader-fixtures /tmp/reader-fixtures
dotnet run --project tools/ExcelRenderer.Performance -c Release -- reader-case /tmp/reader-fixtures/shared.xlsx
```

The reader harness emits one warmup and five measured samples; use medians of
iterations 1–5. It reports ClosedXML/model/total time, total allocation, sampled peak
managed memory, process peak RSS, and available reader counters. Peak managed memory
is sampled at phase callbacks, not a continuous peak. The baseline uses the identical
harness added to its detached worktree; engine source remains at the baseline SHA.
The fixed synthetic source workbooks are shared across both runs.

The committed specimen is `tests/ExcelRenderer.Tests/SampleInputs/ReaderRichText/representative.xlsx`.
Its adjacent README documents regeneration and machine checks. Library output is not
an Excel reference image. **Excel application parity is unverified.**

## Visual evidence and compatibility

![Synthetic partial text output](images/reader-richtext.png)

The PNG was produced from the committed synthetic specimen with the registered
`third_party/NotoSansJP/NotoSansJP-Regular.ttf`, system font discovery disabled, and
trim enabled. The environment printed Fontconfig cache-directory warnings; rendering
completed. This image is library output, not an Excel application reference.

Existing positional constructors and Deconstruct methods are compiled and exercised
by `ExistingConstructorsDeconstructorsAndLegacyLayoutServicesRemainUsable`, including
an old ITextLayoutService implementation. This is source-compatibility evidence,
not a binary compatibility certification against a previously compiled application.

## Local regression results

The implemented library paths rebuilt with zero warnings/errors. The full regression
run passed main 429, Core 76, Mapping 49 and Tool 52 tests (606 total), with zero
failures/skips, including the legacy API/layout-service compatibility and chart tests.
The exact restore/build/test commands were also retried and remain blocked by the
NuGet audit errors above; these passing results use the documented command override.
`python3 scripts/check-core-dependencies.py` passed both dependency direction and shared
ownership checks. Project/package definitions were unchanged, so package-content
validation was not triggered by this work.

The six reader measurements use bounded 10,000-cell source ranges (1,000 for unique
rich text), not whole-sheet style assignments. The harness records model row and column
counts to make this scope visible. Preliminary whole-sheet-styled fixture measurements
were discarded and are not included in the final performance tables.

## Initial measured performance

All values below are medians of five measured iterations after one warmup.

[Aggregate samples and environment](../tools/ExcelRenderer.Performance/results/reader-richtext-20261015/) retain the input hashes, phase counters and each sample.

| Reader input | ClosedXML ms, before → after | Model ms | Total ms | Allocated MiB | Sampled peak managed MiB | Peak RSS MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| hyperlinks | 55.0 → 62.3 | 627.8 → 210.6 | 885.4 → 455.6 | 724.6 → 115.1 | 47.3 → 39.0 | 134.3 → 122.5 |
| shared | 116.7 → 154.0 | 153.8 → 127.4 | 401.1 → 707.0 | 66.9 → 61.5 | 24.5 → 22.3 | 122.7 → 127.4 |
| unique | 1717.4 → 1640.4 | 62.3 → 68.0 | 2093.7 → 1964.0 | 167.4 → 169.1 | 70.6 → 70.8 | 215.7 → 217.3 |
| rich-repeated | 964.4 → 1021.5 | 42.7 → 150.1 | 1068.7 → 1312.0 | 556.3 → 580.8 | 24.1 → 44.6 | 113.6 → 123.6 |
| rich-unique | 228.0 → 193.6 | 11.4 → 24.8 | 264.7 → 255.1 | 64.5 → 66.9 | 21.1 → 21.5 | 101.9 → 100.3 |
| ordinary | 202.8 → 111.8 | 132.3 → 120.6 | 710.9 → 409.6 | 66.9 → 61.5 | 27.7 → 22.3 | 124.0 → 125.4 |

| Input | Scratch workbooks | Style conversions | Cache hits | Cache entries |
| --- | ---: | ---: | ---: | ---: |
| hyperlinks | 1 | 1 | 9999 | 1 |
| shared | 0 | 3 | 9997 | 3 |
| unique | 0 | 10000 | 0 | 8192 |
| rich-repeated | 0 | 1 | 9999 | 1 |
| rich-unique | 0 | 1 | 999 | 1 |
| ordinary | 0 | 3 | 9997 | 3 |

Baseline counter values were not instrumented and are not inferred as measured counts.

The initial shared-style timing increased despite lower model time and allocation. The semantically equivalent ordinary case showed the opposite time change. Rechecking with tiered compilation disabled in both binaries gave:

| Recheck | Before total ms | After total ms | Difference |
| --- | ---: | ---: | ---: |
| shared | 211.2 | 215.8 | +2.2% |
| ordinary | 215.3 | 217.7 | +1.1% |

This sensitivity to process/JIT warmup is why timing ratios are not CI assertions. The original measurements are retained, not replaced. Rich repeated reader allocation increases because effective font objects and run snapshots, previously discarded by the baseline, are retained; its extra model work is a feature cost.

| Full PDF conversion | Before total ms | After total ms | Before allocated MiB | After allocated MiB |
| --- | ---: | ---: | ---: | ---: |
| ordinary | 1408.8 | 1402.8 | 209.1 | 204.9 |
| rich-repeated | 1638.0 | 1745.2 | 725.8 | 796.1 |
| Existing 06-layout-and-pagination (tiered compilation off) | 57.5 | 45.8 | 23.0 | 23.0 |

The repeated rich-text PDF case recorded 19999 layout-cache hits and 1 miss per conversion, including preflight and output drawing. No finished runs were rewrapped during painting.


## Follow-up: security dependency and evaluation-scope evidence

The initial NuGet blocker is now repaired without audit suppression. PDFsharp 6.2.3
requires System.Security.Cryptography.Pkcs >= 8.0.1. Explicit Pkcs 8.0.2 references
in both library projects ensure the dependency is also declared in the shipped main
package (Core is bundled rather than a separate package). Pkcs pulls ASN.1 8.0.2.
[Microsoft advisory](https://github.com/advisories/GHSA-846f-f764-76mx) identifies
8.0.2 as the patched 8.x version. All affected lockfiles, including the separate
Core Validation and Reference tools, were regenerated. The original image-comparison
restore failure was reproduced locally and reported the same NU1902/NU1903 errors.

Normal solution restore, locked restore, rebuild (zero warnings/errors), and the
606-test suite passed with audit enabled after the dependency patch. Main and Tool
Release packs succeeded; the main nuspec references Pkcs 8.0.2, both packages retain
Core, and the Tool contains Pkcs. The Core visual script compared all 17 raster pages
successfully without modifying reference images.

Additional rich-text tests cover shared and inline strings, theme colors, retained
variation selectors, CRLF across manual run boundaries, and single styled rotated
runs. The latter exposed a missing unsupported-format marker, now fixed.

Three tests in FormulaEvaluationScopeTests distinguish the exact public API limits:

1. Evaluating an expression over ordinary cells succeeds without touching an unrelated
   circular sheet; evaluating a dirty formula dependency triggers workbook evaluation.
2. InvalidateFormula marks the selected formula dirty, but reading it or recursively
   evaluating a dependent expression still reaches an unrelated circular sheet.
3. Worksheet.RecalculateAllFormulas leaves a dirty cross-sheet dependency untouched,
   yielding 1 from its blank cache where a full fresh evaluation yields 6.

The upstream [XLCell implementation](https://github.com/ClosedXML/ClosedXML/blob/0.105.1/ClosedXML/Excel/Cells/XLCell.cs#L497-L530)
sets Formula.IsDirty in InvalidateFormula and calls Recalculate(wb, null) in Evaluate.
[CalcContext](https://github.com/ClosedXML/ClosedXML/blob/0.105.1/ClosedXML/Excel/CalcEngine/CalcContext.cs#L98-L125)
returns other sheets' cached values during sheet-only calculation; its recursive path
reads cell.Value. Dependency-tree traversal and the calculation chain are internal.

A minimal possible contract change is to allow whole-workbook recalculation and report
unrelated-sheet failures; another is an upstream public API supporting selected roots
plus transitive dependencies. Neither alternative has been adopted. No scope guarantee
is inferred from dependency-free formulas alone.

## Follow-up: cached display and remaining diagnostic paths

`ExcelReadOptions` / `FormulaDisplayMode` are in `ExcelRenderer.Excel`; RenderRequest,
all four legacy export option types, and Reader path/stream overloads carry ReadOptions.
All five rendering CLI commands accept `--formula-mode`; invalid text is rejected by
command parsing before Mapping, input, or output. Mapping calculation options are untouched.
The Recalculate enum value currently fails fast rather than violating the requested scope;
this is an explicit unfinished contract, not a successful implementation of that mode.

FormulaCacheReader uses OpenXmlReader and at most one cell DOM at a time, keeping only
formula-cache metadata (including shared/array followers). Presence of the original
result element distinguishes missing from zero, false, and saved empty text. Typed
values are snapshotted before any read hooks can change values. Numeric date serials
are formatted directly; the 1904 offset is applied once, avoiding ClosedXML's early-date
load offset discrepancy. Existing scratch resources apply ClosedXML number formatting.

Tests change a precedent after loading and mark the formula dirty: cached still shows
the original 99 and leaves the formula dirty, whereas evaluation would give 52. Other
cases cover zero/false/empty/error/missing, 1900/1904 dates, time, percent/custom format,
external/unknown/dynamic hyperlink formulas, shared/array followers, literal HYPERLINK,
hyperlink-preserve/none, selected diagnostics, and unchanged sinks on suppressed failures.

Chart sheets are reported with the existing UnsupportedDrawingObject code and object
ID chart-sheet, with no fabricated cell/page location. Metadata-only sheet placeholders
allow explicit selection; they are excluded from output and do not add blank pages.
Only-chart-sheet selection returns diagnostics with no artifacts. Explicit page selection
excludes chart sheets because they have no output pages. A pre-existing worksheet cast
in HyperlinkReader was fixed to skip non-worksheet parts.

RenderRequest.TextMeasurer is an optional borrowed measurement service. Legacy services
continue using the cell font; opt-in RichTextLayoutFallback is issued only in selected-page
preflight, respects failure/suppression policy, and is counted once. Rotated unsupported
rich formatting is diagnosed even without finalized horizontal layout. No new measurement
is performed just to collect warnings. Additional tests exercise 0.25pt clipping tolerance,
print-scale/rich-run/shrink/rotation small-font warnings, and hidden-row exclusion.

## Final cached/diagnostic implementation validation

Implementation commit: `85ea6264a2123a5346c61ffb24be88caa02716bb` (cached/diagnostic feature commit `8cc0dbb7db4957299cba4a2cc16f3ffa16224a87`).

Normal restore and the required rebuild passed with zero warnings/errors. The full
regression run passed 657 tests: main 466, Core 80, Mapping 49, Tool 62; zero failures
or skips. Public old constructors/Deconstruct and legacy services remain exercised.
Main and Tool Release packages were created; their bundled Core DLL hashes match.

### Final performance recheck

Same inputs, machine, SDK, Release configuration and fixed font as the original comparison.
Each median excludes one warmup and uses five samples. Final reader samples precede the
no-candidate diagnostic guard; that guard affects only full conversion, not Reader.
The final dependency patch is recorded in final-environment.json.

| Reader | ClosedXML ms | Model ms | Total ms | Allocated MiB | Sampled managed MiB | Peak RSS MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| hyperlinks | 70.0 | 248.9 | 518.9 | 115.3 | 39.3 | 125.2 |
| shared | 90.6 | 129.9 | 332.5 | 61.8 | 22.3 | 125.9 |
| unique | 1713.9 | 83.4 | 2054.0 | 169.3 | 70.8 | 217.7 |
| rich-repeated | 1054.8 | 167.8 | 1305.9 | 582.0 | 45.7 | 123.6 |
| rich-unique | 230.2 | 23.7 | 284.1 | 67.1 | 21.6 | 100.4 |
| ordinary | 97.7 | 146.9 | 386.1 | 61.8 | 22.4 | 126.8 |

Final deterministic reader counters match the earlier table (one scratch for hyperlinks,
three conversions and 9997 hits for mixed shared styles, maximum 8192 cache entries).

| Tiering-disabled reader recheck | Baseline ms | Final ms | Difference |
| --- | ---: | ---: | ---: |
| shared | 211.2 | 226.8 | +7.4% |
| ordinary | 215.3 | 220.1 | +2.2% |

| Full PDF | Baseline ms | Final ms | Baseline allocated MiB | Final allocated MiB |
| --- | ---: | ---: | ---: | ---: |
| ordinary | 1408.8 | 1470.3 | 209.1 | 207.1 |
| rich-repeated | 1638.0 | 1604.5 | 725.8 | 796.7 |
| report | 57.5 | 50.9 | 23.0 | 23.0 |

The final recheck initially exposed 585.9 MiB ordinary-PDF allocation: the new
diagnostic path constructed SheetGeometry on every page even with no formula candidates.
An early return for an empty candidate list fixed this regression; the bad samples are
retained as diagnostic-geometry-regression.jsonl. Final ordinary allocation returns to
the original level. Rich-text allocation retains the documented cost of effective run
styles and snapshots. No timing ratio is asserted in CI.

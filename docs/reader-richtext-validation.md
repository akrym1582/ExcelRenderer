# Reader and partial-text implementation status

This work is **not acceptance-complete for the four-item request**. Formula display
modes are blocked by the required evaluation scope in ClosedXML 0.105.1 and are not
exposed as incomplete public APIs. The original formula behavior remains intact.

## Baseline and environment

- Repository: `akrym1582/ExcelRenderer`.
- Fetched `origin/main`: `06280e538df4ebc49989efcb3f239379ffa1e09b`.
- Original local branch: `work`, clean, identical to fetched main (0/0 divergence).
- Independent baseline worktree: `/tmp/excel-baseline`, detached at that SHA.
- Debian 13 x64, .NET SDK 10.0.401, runtime 10.0.12.
- Implementation commit: `232c4cd6a7277c2824593dd0ceded8c8caa7a7d9`.
- Dependency versions and lock files are unchanged.

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

`RichTextTests.ClosedXmlDirtyDependencyEvaluationCanReachUnrelatedSheet` reproduces
that behavior with a selected sheet containing an ordinary dependency and an
unselected circular cell. The public-API-only, fixed-package requirement therefore
needs an agreed evaluation-scope design before the requested Recalculate contract
can be claimed. No private reflection, custom formula interpreter, unconditional
fallback implementation, or silent whole-workbook substitution was introduced.
CachedValue mode and its complete API/CLI propagation remain unimplemented too;
the formula-mode acceptance group has not passed.

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

Remaining acceptance gaps include chartsheets without worksheet anchors, diagnostic
reporting for caller-supplied legacy measurement-service fallback, the full formula
fixture matrix, and exhaustive theme/inline-string/rotation/IVS/range-title combinations.
These limitations must not be described as completed coverage.

## Reproduction

```sh
dotnet restore ExcelRenderer.slnx -p:NuGetAudit=false
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

## Final measured performance

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


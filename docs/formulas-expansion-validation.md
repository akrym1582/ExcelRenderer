# Formula expansion validation — 2026-10-17

Implementation and local validation are complete. Excel desktop comparison is
unobserved and is **not** reported as passed. Windows execution is delegated to
the configured CI matrix; this session ran Linux/.NET 10 only.

- Start/latest main: `b2d42c9c17293085e29f2f6fde50f9bcea9fa7dc`.
- Validated implementation: `52fd14433a5c1d92516c4c9afbf2913b1b281f70`.
- Branch: `feature/formulas-report-functions-and-sparse-ranges`.
- Movement-only commit: `8a2f971`; namespaces, existing public signatures, dependency
  direction and CachedValue defaults are preserved. [Movement map](source-organization.md).
- Supplied scope and acceptance plan: [retained input](plans/formulas-expansion-plan.md).
- Evidence: [environment](validation/formulas-expansion/environment.json),
  [performance summary](validation/formulas-expansion/performance-summary.json),
  [oracle report](validation/formulas-expansion/oracle-report.json).
  Later evidence/documentation commits do not change the measured C# implementation.

## Implemented behavior

The original 19 functions plus 21 new functions total 40: four conditional
aggregates, exact VLOOKUP/MATCH and scalar-reference INDEX, six date functions and
eight text functions including limited TEXT formatting. Function validation and
dispatch share one registry; family-specific code preserves delayed IF/IFERROR.

Optional ISparseFormulaDataSource and a flat sheet/row/column index avoid scanning
missing cells and keep third-party dense sources compatible. Checked cursors
reject malformed coordinates. Conditional aggregates merge relative positions and
count all-blank intervals with long arithmetic. All reads retain session cycle,
error, cancellation, text and work limits; missing blanks are not cached.

Exact lookup indexes grow only through the searched prefix. Repeated different
keys reuse typed first positions. Wildcards retain ordered first text positions
and stop at the first matching entry. No source enumerator survives a call.
Retention is limited to 16 vectors, 65536 distinct typed values and 131072 indexed
text units; capacity uses streaming fallback. Values beyond the first match and
nonmatching return columns remain unevaluated. Large tables beyond these limits
and wildcard scans still consume work proportional to the inspected prefix.

Date1904 overrides a copied options instance; serial 60 remains the fictitious
1900 leap day. ISO date-only and `yyyy-MM-ddTHH:mm:ss[.fffffff]` input preserves
time fractions; timezone-bearing dates are diagnosed. Text uses the explicit
UTF-16 v1 profile. TEXT, SUBSTITUTE and CONCATENATE check output size before
allocation and retain the original cumulative string budget.

## Executed gates

| Check | Result |
| --- | --- |
| Locked solution restore | Passed; only the new development tool needed a new lock file |
| Formulas/Core ownership and dependency checks | Passed; Formulas remains BCL-only netstandard2.1 |
| Solution Release Rebuild | **0 warnings, 0 errors** |
| .NET solution tests | **1843 passed, 0 failed/skipped** |
| Formula tests | 722 passed, including 498 provenance-tagged fixture cases |
| Core / Mapping / OpenXml / main / CLI tests | 82 / 197 / 188 / 563 / 91 passed |
| Oracle Python tooling | 5 passed, including strict zero-observation failure and typed cache extraction |
| Pure engine / OpenXml / save/reload profile comparisons | **1494 matched, 0 mismatches** |
| Mapping-only input / saved-cache comparisons | 8 matched across 0/1/3/70 rows |
| Solution pack | Passed |
| Isolated NuGet engine / OpenXml / Mapping / main+Core / CLI | All five passed, using new functions and sparse COUNTIF |
| Output regression mutations | All 19 detected; source bytes restored |
| Shared geometry mutations | Both products detected the mutation and passed after restoration |
| Core regression mutations | All four detected and restored |
| Core visual comparisons | 17 raster pages checked successfully |
| Expanded invoice XLSX → PDF/PNG/SVG/Markdown | 0/1/3/70 details passed; 70 details produce two PNG pages |
| PowerShell scripts | Syntax parsed; Excel execution unavailable on Linux |

Full build/test/package/mutation logs and inventories are in
[validation/formulas-expansion](validation/formulas-expansion/).
The visual tool's retained `source_commit` field identifies its historical
reference; `environment.json` records this implementation's revision.
A sample attempt overlapped a rebuild and could not start its deleted executable;
it was rerun successfully after builds stopped. The retained acceptance sample log
contains the completed runs, not that transient attempt.

## Performance acceptance

An identical harness was mirrored into a detached baseline checkout, with only
EXPANSION_BASELINE excluding statistics absent from the old API. Final baseline
then current runs were sequential after builds/tests/consumers finished. Each case
uses one warmup and five measured samples; tables use medians. Raw JSONL retains
result/checksum, allocations and GC counts. Legacy bounded-SUM rows do not measure
GC and report null rather than zero. Managed allocated bytes are not retained
memory or process peak RSS. The original bounded-SUM harness separately records
process peak working set.

| Workload | Current median | Actual source reads / candidates | Managed allocated bytes |
| --- | ---: | ---: | ---: |
| SUM of a full column, 100000 stored values with a value at the column tail | 19.08 ms | 100000 / 100000 | 15698040 |
| Empty full-column COUNTIF | 0.048 ms | 0 / 0; 1048576 blanks processed | 2008 |
| Full-column SUMIFS, 100000 rows in two columns | 45.19 ms | 200000 / 200000 | 32570240 |
| 30000-row VLOOKUP, same key ×100 | 4.97 ms | 30001 / 30000 | 6761520 |
| 30000-row VLOOKUP, different keys ×100 | 5.32 ms | 30100 / 30000 | 6985520 |
| Copy + sparse-index construction, 200000 actual cells | 15.89 ms | One verification read | 22213480 |

The repeated lookup workload inspects 30000 candidates instead of rescanning
3000000. Tests enforce this for different keys with default limits, and enforce
that an early wildcard hit in a 10000-text prefix costs fewer than 100 extra
steps. The stress harness retains its higher lookup limits (10M visits/50M steps),
but observed work is about 30100 visits/91401 steps, below the product defaults.
Lookup retention caps and fallback have dedicated deterministic tests.

An empty 1000-cell range and an empty 1048576-cell column allocate the same 2008
bytes and read zero source cells. A full-sheet blank COUNTIF returns 17179869184
with zero reads/cached values in the deterministic test. Whole-column SUM succeeds
for 10/1000/100000 values using default budgets; a dense external source still
stops at its configured limit and full-column self references remain circular.

The common 100000-value bounded SUM measured 11.06 ms / 15700672 allocated bytes
on baseline and 12.80 ms / 15701112 bytes currently. The checked sparse cursor adds
some per-cell overhead in a fully populated bounded rectangle; it remains O(n) with
essentially unchanged allocation. This tradeoff is retained explicitly rather
than claiming every workload is faster. Baseline full-column SUM fails its work
limit, and new functions are UnsupportedFunction there; those failures are
preserved and never presented as speedup ratios. No wall-clock threshold was added
to CI.

Reproduce from the repository root:

```sh
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- expansion-sparse 10,1000,100000
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- expansion-lookup-varied 1000,10000,30000
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- expansion-mixed 10,1000,100000
python3 tools/ExcelRenderer.Formulas.Performance/summarize-expansion.py docs/validation/formulas-expansion --baseline b2d42c9c17293085e29f2f6fde50f9bcea9fa7dc --revision 52fd14433a5c1d92516c4c9afbf2913b1b281f70
```

Also measured: expansion-lookup, expansion-text-date and legacy sum. The summary
pins harness SHA256 and every raw file SHA256; setup/index construction is
measured separately from query evaluation.

## Excel observation remains incomplete

Excel-observed base cases: **0 / 498**. Mapping: **0 / 4**. The three-pipeline report
has 1470 not-observed and 24 unsupported results, zero Excel matches, and zero
profile mismatches. Strict mode deliberately exits 1 because no genuine Excel
observations or mandatory categories exist. Unknown/v2 Unicode compatibility does
not certify the v1 emoji case. No manifest or compatibility exception was invented.

Approximate search, array INDEX results, dynamic arrays, external workbooks,
volatile functions, locale date parsing and unsupported TEXT formats remain outside
the specified scope. Fine criteria coercion/error behavior and fictitious-date
boundaries are explicit profiles pending Excel measurement.

On Windows with Excel desktop, .NET 10 and Python 3, run once from the repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-formula-oracle.ps1 -WorkDirectory C:\Temp\ExcelRenderer-oracle-new
```

This produces manifests, saved Excel workbooks, extracted typed observations and
strict reports for ordinary and Mapping inputs without changing checked-in
expectations. See [provenance and capture details](formulas-oracle.md). The four
exact Mapping inputs used here are retained in
[Fixtures/Oracle/Mapping](../tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle/Mapping/).

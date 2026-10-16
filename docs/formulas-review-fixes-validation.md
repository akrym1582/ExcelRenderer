# Formula review fixes validation

Implementation follows the supplied 2026-10-16 review-fixes instructions. Work
started from freshly fetched `origin/main`, SHA
`492f07b03f4a92be1e316d0afb939a0a48fb9711`, on branch
`fix/formulas-parser-rounding-string-limits`. No pre-existing changes were present.
The initial implementation and integration tests are commit
`d4b8f26860f7e6adf2d8aa612ae2617708526069`. The final verification target is
`ec174461a17289dadda7c2d1d15432e3e6231318`, including current-version package
validation, the allocation regression and preservation of existing signed zero and empty-sheet-name validation.
Subsequent evidence/documentation changes do not change the tested source.

Environment: Debian GNU/Linux 13, Linux x64, .NET SDK 10.0.401, runtime 10.0.12.
The Formulas assembly builds for .NET Standard 2.1. Existing Windows/Linux CI
matrices and all verification gates are unchanged. Windows execution of these
changes and Excel desktop comparison were **not performed in this environment**.
No result is labelled `excel-observed`. Expected values come from the supplied
decimal rounding specification, hand calculation or the explicit compatibility
profile; the adjacent-double expectations are fixed decimal strings, and the
small decimal grid uses an independent integer quotient/remainder test model.

## Before and after

Actual pre-fix C# results are retained in
[before-reproductions.log](validation/formulas-review-fixes/before-reproductions.log).
The initial nine focused tests had seven failures and two passes, before being
expanded into the final regression suite.

| Case | Baseline C# observation | Corrected result |
| --- | --- | --- |
| `="-"`, `="+"`, `="("` | Syntax diagnostics | Text with the quoted value |
| `ROUNDDOWN(0.29,2)` | 0.28 | 0.29 |
| `ROUNDUP(0.07,2)` | 0.08 | 0.07 |
| `ROUND(1.005,2)` | 1.00 | 1.01 |
| ROUND/ROUNDDOWN negative values rounded to zero | Negative zero (ordinary places); positive zero below -308 places | Same sign bits, explicitly regression tested |
| Five-cell doubling chain, requested single-value limit 8 | Limit API absent; success with length 16 | ResourceLimitExceeded at A5, operator span 2:1 |

The small string harness uses only five cells. Both its source and before/after
output are retained in [the evidence directory](validation/formulas-review-fixes/).
Its warmed evaluation allocated 6,136 bytes before and 7,256 bytes after; tiny
exception/diagnostic overhead exceeds the saved 16-unit string allocation here.
This is a bounded failure check, not evidence of lower total allocations for tiny
strings. A separate regression test warms the AST, source and cell cache, then
rejects an 8,192-unit concatenation with **less than 8,192 bytes** allocated; the
combined string alone would require over 16 KiB. No large exponential string was
created. Source inspection also confirms the long length check precedes Concat.

## Changes and coverage

| Files | Change and relevant coverage |
| --- | --- |
| `FormulaParser.cs` | Immutable tokens carry explicit kind, decoded value and original SourceSpan. Every operator/delimiter/reference decision checks kind. Empty Text differs from End, and quoted range endpoints are rejected. |
| `DecimalRounding.cs`, `FormulaSession.cs` | Only rounding decomposes shortest invariant R text into decimal coefficient/exponent and uses BigInteger quotient/remainder. Integer/no-change/extreme-place exits bound work; Number remains double. |
| `FormulaEvaluationOptions.cs`, `FormulaSession.cs` | MaxStringLength=32767 and MaxCachedStringCharacters=16777216, copied and validated on session creation. Check evaluated Text and combined long lengths before allocation; count each cached cell once before registration. |
| `ReviewRegressionTests.cs` | All quoted operators/delimiters, AST kinds/counts/spans, IF/SUM/COUNTA, malformed operators/ranges, escaped sheet names, Rewriter; signed rounding cases, fractional/extreme places, overflow/subnormal/max-double/signed zero, adjacent doubles, unchanged decimals, reference inputs and 1,209 integer-model comparisons. |
| `StringLimitTests.cs` | Boundary 8/9, doubling, literals/source/formula/selected IF Text, lazy branches, cache reuse/distinct cells/shared string instances/direct Evaluate, IFERROR separation, UTF-16 surrogate pairs, zero/negative limits, captured options, cancellation, private diagnostics and allocation bound. |
| OpenXml `IntegrationBoundaryTests.cs`, `RecalculationTests.cs` | Shared-formula translation preserves quoted/escaped sheet names and strings; shared recalculation and typed XLSX rounding caches survive save/reload. Custom/default string and cache limits preserve existing destination bytes. |
| Mapping `FormulaMappingTests.cs` | Custom/default string and cache limit failures after mapping preserve destination bytes. Existing expansion/shared-formula regressions also pass. |
| Main `FormulaRecalculationTests.cs` | Corrected decimal and quoted-string results reach Markdown; existing PDF/PNG/SVG/Markdown, selection and Core PDF suites pass. |
| `EngineBenchmark.cs` | Adds decimal ROUND/ROUNDUP/ROUNDDOWN workload where both implementations return the same validated results, exposing conversion/BigInteger costs. |
| `validate-formulas-packages.py` | Reads actual project versions instead of assuming all packages are 1.8.0; isolated engine consumer checks the three fixes. All dependency/DLL assertions remain. |
| `formulas.md`, `formulas.ja.md` | Document the rounding profile, resource defaults, UTF-16 accounting, diagnostic semantics and scope of memory guarantees. |

IF still evaluates only the selected branch while static function validation
covers the whole reached AST. IFERROR catches ordinary Excel error values, never
resource failures. The values dictionary alone carries the cumulative Text budget:
results dictionary reuse and repeated cell reads do not count twice, whereas two
cells sharing one string count twice. Unretained Evaluate(parsed) results do not
consume the cumulative cache budget. Failed parent formulas retain diagnostics,
not incomplete successful values; completed dependencies may remain cached.

## Executed verification

| Check | Result |
| --- | --- |
| Locked solution restore | Passed, lockfiles unchanged |
| Formulas and Core dependency/ownership scripts | Passed |
| Final Release solution Rebuild | 0 warnings, 0 errors |
| Final solution test run | **1,297 passed, 0 failed, 0 skipped** |
| Formulas / OpenXml / Mapping | 192 / 180 / 193 passed |
| Core / main / CLI | 82 / 559 / 91 passed |
| Output / shared Core / Core mutations | 19/19, 2/2, 4/4 detected; original sources restored |
| Core visual comparison | 17 raster pages, maximum baseline MAE 0.0; unchanged 0.25 threshold |
| Local solution pack | Six packages: Formulas/OpenXml 1.8.0; main/Fonts/Mapping/Tool 1.9.0 |
| Fresh isolated package consumers | Engine, adapter, Mapping, main + bundled Core, installed CLI passed |

Logs, project test counts, mutation source hashes, visual report and package
inventory are retained in [validation/formulas-review-fixes/](validation/formulas-review-fixes/).

The commands below ran from the repository root. Network-enabled execution was
needed for NuGet/Git and VSTest's local communication socket; an initial sandboxed
VSTest attempt aborted before running tests, then the same tests were executed
with socket access. Locked restore passed with no lockfile changes. No warnings
were silenced, no tests excluded, no thresholds relaxed, and no new dependencies
were added.

```sh
git status --short
git fetch origin
git switch -c fix/formulas-parser-rounding-string-limits origin/main
git rev-parse HEAD
dotnet restore ExcelRenderer.slnx --locked-mode
python3 scripts/check-formulas-dependencies.py
python3 scripts/check-core-dependencies.py
dotnet test tests/ExcelRenderer.Formulas.Tests -c Release --no-restore
dotnet test tests/ExcelRenderer.OpenXml.Tests -c Release --no-restore
dotnet test tests/ExcelRenderer.Mapping.Tests -c Release --no-restore
source scripts/test-fonts.sh
python3 scripts/check-output-mutations.py
python3 scripts/check-core-shared-mutations.py
python3 scripts/check-core-mutations.py
python3 scripts/validate-core.py --visual-only
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal
dotnet test ExcelRenderer.slnx -c Release --no-build
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -p:BuildProjectReferences=false -o artifacts/formulas-review-packages
python3 scripts/validate-formulas-packages.py --packages artifacts/formulas-review-packages --work /tmp/formulas-review-package-consumers-final-parser
```

A full build-and-test run also passed before the final Rebuild/no-build run. The
final test count includes the allocation and signed-zero regressions. The historical
[formulas validation](formulas-validation.md) and performance results are preserved
for their original SHAs. The new visual report preserves golden-image provenance
in source_commit and records its run at `558971e` separately in validation_commit.
Mutation and visual gates ran before the final signed-zero and empty-sheet-name follow-ups; their
renderer and geometry sources are byte-identical at the final verification SHA.

## Performance

At 30,000 rows, XLSX recalculation/save changed by -0.4%
and Mapping/recalculate/save by +15.3%. Managed allocation changes were
+0.15% and +0.20%, respectively.
Actual decimal rounding costs 2.72–2.93x the old binary-scaling implementation
on these 30,000-operation cases, allocating about 1.76x as much.
This additional conversion/BigInteger cost is explicit. Integer and decimal
no-change exits are present; correctness is not traded for the old rounding path.
Engine sessions also include typed tokens and propagated source spans; complete
size-by-size figures are below. These local timings do not establish general overhead or speedup.
Both baseline and final captures ran sequentially without concurrent repository
builds, tests or package-consumer work. The final captures include the signed-zero and empty-sheet-name
compatibility changes.

| Case | Rows | Before ms | After ms | Time ratio | Before MiB | After MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| engine-common-dependency | 1000 | 1.15 | 1.11 | 0.969 | 1.28 | 1.29 |
| engine-common-dependency | 10000 | 11.98 | 13.10 | 1.094 | 12.51 | 12.67 |
| engine-common-dependency | 30000 | 39.46 | 54.53 | 1.382 | 34.94 | 35.40 |
| engine-constant-repeated | 1000 | 1.04 | 0.93 | 0.890 | 0.35 | 0.35 |
| engine-constant-repeated | 10000 | 9.36 | 9.44 | 1.009 | 3.43 | 3.43 |
| engine-constant-repeated | 30000 | 28.61 | 29.94 | 1.046 | 10.30 | 10.30 |
| engine-independent | 1000 | 2.67 | 2.60 | 0.971 | 2.53 | 2.54 |
| engine-independent | 10000 | 30.10 | 29.19 | 0.970 | 25.03 | 25.18 |
| engine-independent | 30000 | 92.94 | 96.30 | 1.036 | 72.48 | 72.94 |
| xlsx-recalculate-save | 1000 | 31.96 | 32.70 | 1.023 | 10.87 | 10.89 |
| xlsx-recalculate-save | 10000 | 349.98 | 376.93 | 1.077 | 104.57 | 104.72 |
| xlsx-recalculate-save | 30000 | 1167.60 | 1163.07 | 0.996 | 308.15 | 308.61 |
| mapping-expand-recalculate-save | 1000 | 56.30 | 56.97 | 1.012 | 22.56 | 22.60 |
| mapping-expand-recalculate-save | 10000 | 554.28 | 555.98 | 1.003 | 199.26 | 199.64 |
| mapping-expand-recalculate-save | 30000 | 1678.68 | 1935.46 | 1.153 | 579.36 | 580.51 |
| engine-decimal-round | 1000 | 0.50 | 1.36 | 2.743 | 0.22 | 0.39 |
| engine-decimal-round | 10000 | 4.68 | 13.12 | 2.804 | 2.21 | 3.89 |
| engine-decimal-round | 30000 | 13.91 | 40.76 | 2.930 | 6.64 | 11.67 |
| engine-decimal-rounddown | 1000 | 0.46 | 1.40 | 3.020 | 0.22 | 0.39 |
| engine-decimal-rounddown | 10000 | 4.96 | 13.20 | 2.660 | 2.21 | 3.89 |
| engine-decimal-rounddown | 30000 | 14.34 | 39.80 | 2.775 | 6.64 | 11.67 |
| engine-decimal-roundup | 1000 | 0.45 | 1.42 | 3.136 | 0.22 | 0.39 |
| engine-decimal-roundup | 10000 | 4.71 | 12.87 | 2.734 | 2.21 | 3.89 |
| engine-decimal-roundup | 30000 | 14.64 | 39.83 | 2.721 | 6.64 | 11.67 |
The comparison follows [the existing method](formulas-performance.md): one warmup,
five measured repetitions, median time and managed allocations, TieredCompilation=0,
1000/10000/30000 rows, same harness source and numerical assertions on both SHAs.
The baseline uses an isolated worktree at the freshly fetched starting SHA.
`Baseline=true` is deliberately omitted because that older harness switch removes
engine/integration workloads; RepositoryRoot alone selects the baseline libraries.
All 24 cases per implementation validate every evaluated/saved result. With both
implementations there are 288 executions, including 240 measured samples.

Original engine/integration fixtures produce 22 per detail and 22*rows for SUM;
the integer result is a proven no-rounding fast path. The added decimal workload
uses `ROUND(12.345,2)=12.35`, `ROUNDUP(12.341,2)=12.35` and
`ROUNDDOWN(12.349,2)=12.34`, where both versions produce correct results. Known
incorrect baseline cases are tested for correctness, never used for speed ratios.
Allocation counts include all managed threads; process peak working set is a
lifetime high-water mark, not incremental retention or post-GC RSS. Measurements
are local observations and do not establish general performance or Excel parity.

```sh
export DOTNET_TieredCompilation=0
git worktree add --detach /tmp/formulas-review-baseline 492f07b03f4a92be1e316d0afb939a0a48fb9711
dotnet build tools/ExcelRenderer.Formulas.Performance -c Release -p:RepositoryRoot=/tmp/formulas-review-baseline
# Capture each DLL execution in a separate before-<mode>.jsonl file.
for mode in engine recalculate mapping rounding; do
  dotnet tools/ExcelRenderer.Formulas.Performance/bin/Release/net10.0/ExcelRenderer.Formulas.Performance.dll "$mode" 1000,10000,30000
done
dotnet restore tools/ExcelRenderer.Formulas.Performance --locked-mode
dotnet build tools/ExcelRenderer.Formulas.Performance -c Release --no-restore
# Capture again in after-<mode>.jsonl files, each in a new process.
for mode in engine recalculate mapping rounding; do
  dotnet tools/ExcelRenderer.Formulas.Performance/bin/Release/net10.0/ExcelRenderer.Formulas.Performance.dll "$mode" 1000,10000,30000
done
```

## Remaining compatibility limits

This rounds the input double's shortest decimal representation, rather than
emulating undocumented Excel precision corrections. It deliberately preserves
computed 0.30000000000000004 and does not apply epsilon or global 15-digit trimming.
Resource limits use UTF-16 units, with conservative per-cell logical retention,
not string-instance identity. Caller-owned data, adapter snapshots, total process
memory and resident memory after GC are outside this budget. Final destination I/O
failure can still leave partial output under the existing stream contract.

The existing function set, dependency direction, package boundaries and normal
Excel error versus engine diagnostic distinction remain unchanged. Packages are
local artifacts only. No package was published, no branch pushed and no main
merge performed. Windows and Excel desktop execution remain unverified locally;
the unchanged Windows/Linux CI jobs are available for subsequent remote validation.

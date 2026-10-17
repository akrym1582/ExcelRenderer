# Three formula review fixes — validation (2026-10-17)

Latest `origin/main` was fetched successfully. Base SHA:
`b5539b2802e6b2417035313cafffc5dd53529c25`.
Implementation: working tree on `fix/formula-oracle-range-review`; the checkout
was clean before work. Runtime: .NET SDK 10.0.401 / runtime 10.0.12, Debian 13,
Linux x64. [Environment and harness hashes](environment.json) and
[SDK details](dotnet-info.txt) identify this run.

## Changes and regression coverage

1. Oracle stages execute separately and record one result each. Expected formula
   exceptions produce only that stage's Diagnostic; unexpected exceptions retain
   the original exception plus case ID and pipeline. OpenXml and Mapping evaluations
   each open their own input independently of roundtrip. Delegate tests cover all
   success/failure combinations and an omitted roundtrip. Real XLSX tests cover a
   successful target evaluation followed by a roundtrip-only unsupported formula,
   for both normal and Mapping paths. Python rejects identical and conflicting
   duplicate actual keys, while accepting the same ID on different pipelines.
2. The immutable index now also stores ascending row arrays by column. Missing
   columns return immediately and single-column ranges lower-bound only their own
   array: O(log Ncol + K). Multi-column ranges retain row-major scanning and deduplication.
   No values/results are copied; additional retained coordinates use O(stored cells).
   `IndexWork` counts dictionary probes, binary-search comparisons, inspected rows
   and coordinates, including dense coverage proofs. Each unit checks cancellation
   and consumes one evaluation step; there is no batching overshoot. Original reads
   and candidates retain their meanings. Deterministic tests cover empty/1/3-cell
   columns against 10/2000 unrelated rows, bounded ranges, duplicate coordinates,
   case-insensitive sheets, non-returning scans, coverage limits, and cancellation
   during session MoveNext before any candidate. Existing SUMIFS, MATCH/VLOOKUP,
   prefix cache and dense/sparse equivalence tests pass. Internal test access adds
   no public test API or project references.
3. A single parsed target is used throughout the normal engine/OpenXml/roundtrip
   paths. C5 and AA10 fixtures both yield Number 5 with A1=2, B1=3 and unrelated
   Z1=999. Tests also verify saved Z1 remains 999, the original Z1 path, Mapping's
   original sheet, invalid/missing targets and target/input overlaps. Both Python
   generation and C# execution reject overlaps. New expectations are hand-calculated.
   [Input hashes](inputs.json), actuals and comparison records were regenerated for
   the new 500-case SHA; historical reports and Excel observations were not edited.

## Executed checks

- Locked restore: passed ([log](locked-restore.log)).
- Release solution Rebuild: **0 warnings, 0 errors** ([log](rebuild.log)).
- Full solution tests: **1972 passed, 0 failed, 0 skipped** ([log](tests.log),
  [TRX-derived suite summary](test-summary.json)).
  - ExcelRenderer.Core.Tests: 82.
  - ExcelRenderer.Formulas.Tests: 833.
  - ExcelRenderer.Mapping.Tests: 197.
  - ExcelRenderer.OpenXml.Tests: 206.
  - ExcelRenderer.Tests: 563.
  - ExcelRenderer.Tool.Tests: 91.
- Python Oracle tests: **8 passed** ([log](oracle-tool-tests.log)).
- Namespace and formula dependency checks: passed ([namespaces](namespaces.log),
  [dependencies](dependencies.log)); publication allowlist: 21 projects checked,
  only the existing four packages packable. All four Release packages packed
  successfully ([log](pack.log)); distribution rules/project references unchanged.
- Normal Oracle: **1500/1500 profile checks matched**, 0 mismatches; 24 expected
  diagnostics, 1476 unobserved ordinary results ([report](oracle-report.json),
  [actuals](actual.json)). Mapping: **8/8 matched**, 0 mismatches
  ([report](mapping-oracle-report.json), [actuals](mapping-actual.json)).
- Strict Oracle: **exit 1 as expected**, with 0 profile mismatches and 0 Excel
  observations ([report](oracle-strict-report.json), [exit](oracle-strict-exit.json)).

Earlier exploratory validation overlapped a rebuild with CLI tests, causing the
CLI assembly to disappear temporarily (52 failures). Those runs and concurrent
performance measurements are not acceptance evidence. The final run above follows
the completed rebuild; baseline/current benchmarks then run sequentially.
The sandbox initially blocked local MSBuild/test sockets; execution was repeated
with the environment's network permission. Git fetch and restore succeeded through
the configured proxy. No code workaround or analyzer suppression was added.

## Performance

The same harness runs on the base and updated engine with 100000 and 1000000
A-column cells and 0/1/3 B-column cells. Coordinates and parsing are prepared outside
evaluation measurements; coordinate index construction is measured separately.
Evaluation uses an indexed synthetic source returning Number 7 for stored coordinates,
with origin S!Z1 outside all queried ranges. Each result is checked, with one warmup
and five measured repetitions; tables report medians. Allocation uses
`GC.GetTotalAllocatedBytes(true)`. There are no elapsed-time CI thresholds.

| A rows | B cells | Operation | Base ms | Updated ms | Updated IndexWork | Target reads |
| ---: | ---: | --- | ---: | ---: | ---: | ---: |
| 100000 | 0 | `COUNTIF(B:B,"")` | 0.8252 | 0.0161 | 4 | 0 |
| 100000 | 1 | `COUNTIF(B:B,"")` | 0.8333 | 0.0280 | 6 | 1 |
| 100000 | 3 | `SUM(B:B)` | 2.6742 | 0.0182 | 9 | 3 |
| 100000 | 0 | `SUM(A1:A100000)` | 29.1885 | 29.0457 | 2 | 100000 |
| 100000 | 0 | `SUMIFS(A1:A100000,A1:A100000,">0")` | 29.2938 | 27.2779 | 4 | 100000 |
| 100000 | 0 | `MATCH(7,A:A,0)` | 0.0187 | 0.0250 | 22 | 1 |
| 100000 | 0 | `VLOOKUP(7,A:B,1,FALSE)` | 0.0296 | 0.0240 | 22 | 1 |
| 1000000 | 0 | `COUNTIF(B:B,"")` | 6.7351 | 0.0205 | 4 | 0 |
| 1000000 | 1 | `COUNTIF(B:B,"")` | 6.5046 | 0.0364 | 6 | 1 |
| 1000000 | 3 | `SUM(B:B)` | 6.5231 | 0.0215 | 9 | 3 |
| 1000000 | 0 | `SUM(A1:A1000000)` | 128.8532 | 104.3389 | 2 | 1000000 |
| 1000000 | 0 | `SUMIFS(A1:A1000000,A1:A1000000,">0")` | 189.4820 | 178.2371 | 4 | 1000000 |
| 1000000 | 0 | `MATCH(7,A:A,0)` | 0.0196 | 0.0320 | 25 | 1 |
| 1000000 | 0 | `VLOOKUP(7,A:B,1,FALSE)` | 0.0163 | 0.0377 | 25 | 1 |

| A rows (empty B) | Build base ms | Build updated ms | Allocated base bytes | Allocated updated bytes | Additional build allocation |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 100000 | 13.0368 | 15.2112 | 5797224 | 7246672 | 1449448 |
| 1000000 | 43.7094 | 49.2401 | 49557904 | 61947456 | 12389552 |

Empty-B COUNTIF returns 1048576 with no target reads; one-cell B returns 1048575
with one read. SUM of three B values returns 21 with three reads. Their IndexWork
is 4/6/9 at either unrelated row count. Dense SUM and SUMIFS retain direct traversal,
with only 2/4 index units for coverage proofs. MATCH/VLOOKUP still read only one
value on an early match. Dense timing differences and all evaluation allocations
are retained in [the full summary](performance-summary.json), including all B
layouts. Small absolute lookup timings fluctuate; deterministic work and correctness
checks, rather than timing ratios, establish the bounds. Per-operation accounting
was retained because dense paths perform constant index work; batching is unnecessary.

The added retained row arrays require four bytes per unique stored coordinate
(about 4 MB for a million cells), plus per-column dictionary/array headers. This is
a layout calculation, not a measured live-heap total. Measured build allocations
include temporary growing row lists and copied arrays. The tables explicitly show
the construction/allocation tradeoff rather than attributing it to evaluation.
Raw repetitions: [base](baseline-range-index.jsonl), [updated](current-range-index.jsonl).

## Reproduction commands

From the repository root (source `scripts/test-fonts.sh` for rendering/CLI tests):

```sh
dotnet restore ExcelRenderer.slnx --locked-mode -m:1
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore -m:1 --verbosity:minimal
source scripts/test-fonts.sh
sha256sum -c tests/ExcelRenderer.Tests/Fonts/SHA256SUMS
dotnet test ExcelRenderer.slnx -c Release --no-build --no-restore -m:1 --logger trx --results-directory TestResults/FormulasReviewComplete
python3 scripts/check-source-namespaces.py
python3 scripts/check-formulas-dependencies.py
python3 scripts/package_distribution.py
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -m:1 -p:BuildProjectReferences=false -o /tmp/excelrenderer-review-packages
python3 tools/ExcelRenderer.Formulas.Oracle/test_oracle.py
python3 tools/ExcelRenderer.Formulas.Oracle/oracle.py generate /tmp/excelrenderer-review-inputs
dotnet run --project tools/ExcelRenderer.Formulas.Oracle -c Release --no-build -- tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle/cases.json docs/validation/formulas-review/actual.json /tmp/excelrenderer-review-inputs /tmp/excelrenderer-review-roundtrip
python3 tools/ExcelRenderer.Formulas.Oracle/oracle.py compare docs/validation/formulas-review/actual.json --require-roundtrip --output docs/validation/formulas-review/oracle-report.json
dotnet run --project tools/ExcelRenderer.Formulas.Oracle -c Release --no-build -- --mapping tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle/Mapping/cases.json docs/validation/formulas-review/mapping-actual.json tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle/Mapping /tmp/excelrenderer-review-mapping-roundtrip
python3 tools/ExcelRenderer.Formulas.Oracle/oracle.py --cases tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle/Mapping/cases.json compare docs/validation/formulas-review/mapping-actual.json --require-roundtrip --output docs/validation/formulas-review/mapping-oracle-report.json
# Expected exit 1 because no Excel observations exist:
python3 tools/ExcelRenderer.Formulas.Oracle/oracle.py compare docs/validation/formulas-review/actual.json --require-roundtrip --strict --output docs/validation/formulas-review/oracle-strict-report.json
```

For the base performance run, create a detached worktree at the recorded base SHA,
copy only `tools/ExcelRenderer.Formulas.Performance/{Program.cs,RangeIndexBenchmark.cs}`
from the updated checkout, then run:

```sh
dotnet restore /tmp/excelrenderer-review-baseline/tools/ExcelRenderer.Formulas.Performance --locked-mode -m:1
dotnet build /tmp/excelrenderer-review-baseline/tools/ExcelRenderer.Formulas.Performance -c Release --no-restore -m:1 -p:ExpansionBaseline=true
dotnet run --project /tmp/excelrenderer-review-baseline/tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- range-index 100000,1000000
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- range-index 100000,1000000
```

## Remaining limits

Excel desktop capture was not run because Excel is unavailable in this Linux
environment. Profile matches and hand calculations do not establish Excel agreement.
Windows validation was not run here. The internal-work/cancellation guarantee covers
built-in index scans; arbitrary external iterator code cannot be forcibly interrupted
inside MoveNext. Multi-column ranges intentionally retain the existing row index.
No new formula functions were added; existing API namespaces, package structure
and ProjectReferences remain unchanged. Isolated package consumer tests and visual/mutation suites were not
rerun because this change does not alter distribution, rendering or source ownership;
all rendering/Core/Mapping/CLI regression projects were run in the full solution test.

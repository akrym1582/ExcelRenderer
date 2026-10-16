# Formula performance validation

The reproducible executable is `tools/ExcelRenderer.Formulas.Performance`.
No timing threshold is enforced in CI. All workloads validate their numerical results;
a mismatch terminates the executable rather than producing a successful timing.
The baseline is detached `origin/main` commit
`d7c08f464a2be2b1d8f20d71607b6e0f04c1cfc4`.

## Method

Each case has one warmup and five measured repetitions. `DOTNET_TieredCompilation=0`
is explicitly set for both baseline and modified processes to avoid tier-promotion
changes between small and large workloads. GC collection runs before
each repetition, outside the timer. The timer includes the complete operation and
its correctness assertions. Inputs and in-memory immutable snapshots are built
outside the timer. Allocations use `GC.GetTotalAllocatedBytes(true)` and include
all managed threads. Peak working set is the operating system's process lifetime
high-water mark, **not** per-case retained memory; larger cases run after smaller
ones in the same process. Raw JSON lines retain all warmups, individual samples,
counts, checksums, OS/runtime and processor count. These microbenchmarks do not
measure PDF/PNG rendering time and do not establish Excel compatibility.

The reader cases use the same synthetic workbook on baseline and modified code:
one numeric input and `ROUND(Arow*10*1.1,2)` per detail row, with a final SUM.
Every detail result is 22; the total is `22 * rows`. The cached reader checks the
formula count and the sum of displayed formula values (`44 * rows`).
The XLSX recalculation case recalculates, saves and reopens XML to check **every**
typed numeric cache. Mapping starts with one template detail row and expands it
before recalculation and cache verification. The no-recalculation Mapping case
uses the same template but checks that every unevaluated formula cache is removed.
This explicitly enables formula rewriting; legacy CachedValue Mapping still
rejects formula-bearing repeated rows.

Engine measurements create a fresh shared evaluation session per repetition,
never a separate session per formula cell. Independent and common-dependency
cases check each result; the SUM case visits the requested number of constant
cells. The repeated-constant case reuses a parsed AST within one session.

## Reproduction

Run from the repository root with .NET 10:

```sh
export DOTNET_TieredCompilation=0
git worktree add --detach /tmp/excel-formulas-baseline d7c08f464a2be2b1d8f20d71607b6e0f04c1cfc4
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release -p:Baseline=true -p:RepositoryRoot=/tmp/excel-formulas-baseline -- reader 1000,10000,30000
# Restore default project references after the baseline build.
dotnet restore tools/ExcelRenderer.Formulas.Performance
dotnet build tools/ExcelRenderer.Formulas.Performance -c Release
# Run each command in a new process and retain stdout as a .jsonl file.
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- reader 1000,10000,30000
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- reader-recalculate 1000,10000,30000
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- engine 1000,10000,30000
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- sum 100000
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- recalculate 1000,10000,30000
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- mapping-no-recalculate 1000,10000,30000
dotnet run --project tools/ExcelRenderer.Formulas.Performance -c Release --no-build -- mapping 1000,10000,30000
python3 tools/ExcelRenderer.Formulas.Performance/summarize.py
```

Do not run the baseline build concurrently with the modified build because they
share the benchmark project's output folder. Build output must not be included
in JSON records (build first, then execute the resulting DLL when capturing).

## Recorded results

The captured run used Debian GNU/Linux 13 (trixie), .NET SDK 10.0.401,
.NET runtime 10.0.12, and four reported processors on 2026-10-16.
Raw records are under `tools/ExcelRenderer.Formulas.Performance/results/`.
The table reports medians of the five non-warmup samples. Baseline and current
runs share the same fixture and assertions. The measured implementation is commit
`56d7eb575c90fb0575e86174055ceb02a6d42b48`; subsequent documentation commits
record this evidence. The baseline is `d7c08f464a2be2b1d8f20d71607b6e0f04c1cfc4`.
All cases were repeated sequentially in a reserved measurement window after
local builds, tests, mutation checks and packaging had completed. No concurrent
repository build or test ran during this final capture. Timing remains specific
to this container and fixture; these measurements do not establish a speedup for
other workbooks. All 28 cases completed six validated executions (168 total,
including 140 measured repetitions).

| Run | Case | Count | Median ms | Median allocated MiB | Max process peak MiB |
|---|---|---:|---:|---:|---:|
| baseline-d7c08f4-reader | reader-cached | 1000 | 26.05 | 9.93 | 67.45 |
| baseline-d7c08f4-reader | reader-cached | 10000 | 266.62 | 91.65 | 145.61 |
| baseline-d7c08f4-reader | reader-cached | 30000 | 781.57 | 268.97 | 203.66 |
| current-engine | engine-constant-repeated | 1000 | 1.10 | 0.35 | 30.51 |
| current-engine | engine-independent | 1000 | 2.71 | 2.53 | 32.43 |
| current-engine | engine-common-dependency | 1000 | 1.13 | 1.28 | 32.66 |
| current-engine | engine-constant-repeated | 10000 | 10.24 | 3.43 | 34.20 |
| current-engine | engine-independent | 10000 | 28.14 | 25.03 | 59.19 |
| current-engine | engine-common-dependency | 10000 | 11.47 | 12.51 | 59.69 |
| current-engine | engine-constant-repeated | 30000 | 30.40 | 10.30 | 59.69 |
| current-engine | engine-independent | 30000 | 87.34 | 72.48 | 112.64 |
| current-engine | engine-common-dependency | 30000 | 35.42 | 34.94 | 117.51 |
| current-mapping-no-recalculate | mapping-expand-invalidate-save | 1000 | 20.93 | 11.15 | 66.98 |
| current-mapping-no-recalculate | mapping-expand-invalidate-save | 10000 | 166.51 | 89.08 | 104.79 |
| current-mapping-no-recalculate | mapping-expand-invalidate-save | 30000 | 497.84 | 254.99 | 202.99 |
| current-mapping | mapping-expand-recalculate-save | 1000 | 52.44 | 22.56 | 78.54 |
| current-mapping | mapping-expand-recalculate-save | 10000 | 540.13 | 199.24 | 198.07 |
| current-mapping | mapping-expand-recalculate-save | 30000 | 1620.81 | 579.35 | 220.83 |
| current-reader-recalculate | reader-recalculate | 1000 | 36.56 | 16.23 | 74.55 |
| current-reader-recalculate | reader-recalculate | 10000 | 394.22 | 153.57 | 156.46 |
| current-reader-recalculate | reader-recalculate | 30000 | 1307.53 | 448.80 | 234.12 |
| current-reader | reader-cached | 1000 | 24.78 | 9.93 | 68.06 |
| current-reader | reader-cached | 10000 | 265.42 | 91.65 | 146.21 |
| current-reader | reader-cached | 30000 | 775.70 | 268.97 | 204.26 |
| current-recalculate | xlsx-recalculate-save | 1000 | 32.59 | 10.87 | 64.48 |
| current-recalculate | xlsx-recalculate-save | 10000 | 343.38 | 104.57 | 117.59 |
| current-recalculate | xlsx-recalculate-save | 30000 | 1164.93 | 308.15 | 203.14 |
| current-sum | engine-sum | 100000 | 14.27 | 14.97 | 74.35 |

At 30,000 rows the CachedValue path allocated the same approximately 269 MiB
on baseline and current implementation; measured medians were 781.57 ms and
775.70 ms respectively (current/baseline 0.9925). At 1,000 and 10,000 rows the
time ratios were 0.951 and 0.996, also with unchanged allocations to the table's
precision. These observations support no material regression in this fixture;
the small differences do not establish a general speedup.

Mapping expansion without recalculation allocated 11.15 / 89.08 / 254.99 MiB
for 1k / 10k / 30k rows; with recalculation it allocated
22.56 / 199.24 / 579.35 MiB. Both paths checked all formula cells, including
absence of unevaluated caches in the no-recalculation case and numeric cache
types in the recalculation case. The 100,000-cell SUM returned exactly 200,000,
at 14.27 ms and 14.97 MiB allocated. All 30,000 independent/common-dependent
formulas returned 22, with checksums 660,000.

For the 10k to 30k increase (3x workload), elapsed-time ratios were 3.10x for
independent formulas, 3.09x for common dependencies, 3.00x for Mapping with
recalculation, 2.99x for Mapping without recalculation, 3.32x for the recalculating
reader, and 3.39x for XLSX recalculation/save. The corresponding allocation ratios
were approximately 2.90x, 2.79x, 2.91x, 2.86x, 2.92x, and 2.95x. These measurements
show roughly linear scaling across the measured range, rather than the 9x cost
expected of a quadratic algorithm for a 3x workload. They do not prove asymptotic
complexity for all formulas or workbooks. Bounded shared-session evaluation avoids
repeatedly evaluating the common dependency; the harness never performs a new
independent recalculation session per formula cell.

Peak working-set values include process history and cannot be subtracted as
incremental costs. No unbounded full-column materialization is part of these
cases. Excel desktop was not used for these checks.


## Review-fix comparison (2026-10-16)

A separate [review-fix validation](formulas-review-fixes-validation.md#performance)
compares freshly fetched main `492f07b` with `ec17446`, retaining the historical
measurements above. It runs engine, XLSX recalculation/save, Mapping/recalculation/save
and additional decimal ROUND/ROUNDUP/ROUNDDOWN cases at 1k/10k/30k, with the same
warmup/five-sample method and per-result assertions. At 30k, XLSX time changed
-0.4% and Mapping time +15.3%; allocations changed
+0.15% and +0.20%. Actual decimal rounding was
2.72–2.93x slower, with approximately 1.76x allocations. The explicit
shortest-decimal rounding profile adds conversion/BigInteger work; proven integer
and no-change exits avoid it where rounding is unnecessary. All raw samples and
all 24 median comparisons are retained under `docs/validation/formulas-review-fixes/`.

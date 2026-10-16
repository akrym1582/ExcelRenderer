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
runs share the same fixture and assertions. The current result was measured from
the working implementation; the final PR commit identifies the delivered source.
Other repository builds/tests shared this container, so wall-time variation is
not evidence of an implementation speedup. Allocation trends are more stable.

| Run | Case | Count | Median ms | Median allocated MiB | Max process peak MiB |
|---|---|---:|---:|---:|---:|
| baseline-d7c08f4-reader | reader-cached | 1000 | 27.55 | 9.93 | 67.31 |
| baseline-d7c08f4-reader | reader-cached | 10000 | 318.83 | 91.65 | 145.36 |
| baseline-d7c08f4-reader | reader-cached | 30000 | 808.87 | 268.97 | 203.42 |
| current-engine | engine-constant-repeated | 1000 | 1.02 | 0.14 | 29.90 |
| current-engine | engine-independent | 1000 | 6.35 | 2.53 | 32.12 |
| current-engine | engine-common-dependency | 1000 | 1.96 | 1.28 | 32.35 |
| current-engine | engine-constant-repeated | 10000 | 10.19 | 1.37 | 33.92 |
| current-engine | engine-independent | 10000 | 74.22 | 25.03 | 59.10 |
| current-engine | engine-common-dependency | 10000 | 57.38 | 12.51 | 59.69 |
| current-engine | engine-constant-repeated | 30000 | 68.24 | 4.12 | 59.69 |
| current-engine | engine-independent | 30000 | 464.68 | 72.48 | 112.51 |
| current-engine | engine-common-dependency | 30000 | 64.42 | 34.94 | 117.37 |
| current-mapping-no-recalculate | mapping-expand-invalidate-save | 1000 | 19.19 | 11.15 | 67.22 |
| current-mapping-no-recalculate | mapping-expand-invalidate-save | 10000 | 154.16 | 89.09 | 105.01 |
| current-mapping-no-recalculate | mapping-expand-invalidate-save | 30000 | 463.84 | 255.00 | 203.34 |
| current-mapping | mapping-expand-recalculate-save | 1000 | 51.94 | 22.56 | 78.63 |
| current-mapping | mapping-expand-recalculate-save | 10000 | 617.41 | 199.25 | 198.17 |
| current-mapping | mapping-expand-recalculate-save | 30000 | 2548.30 | 579.35 | 250.58 |
| current-reader-recalculate | reader-recalculate | 1000 | 38.96 | 15.92 | 73.93 |
| current-reader-recalculate | reader-recalculate | 10000 | 408.22 | 150.52 | 155.54 |
| current-reader-recalculate | reader-recalculate | 30000 | 2967.37 | 439.64 | 238.00 |
| current-reader | reader-cached | 1000 | 26.36 | 9.93 | 67.96 |
| current-reader | reader-cached | 10000 | 285.76 | 91.65 | 146.11 |
| current-reader | reader-cached | 30000 | 814.60 | 268.97 | 204.16 |
| current-recalculate | xlsx-recalculate-save | 1000 | 61.25 | 10.87 | 64.52 |
| current-recalculate | xlsx-recalculate-save | 10000 | 365.94 | 104.57 | 117.82 |
| current-recalculate | xlsx-recalculate-save | 30000 | 1117.63 | 308.15 | 203.36 |
| current-sum | engine-sum | 100000 | 12.75 | 14.97 | 74.59 |

At 30,000 rows the CachedValue path allocated the same approximately 269 MiB
on baseline and current implementation; measured medians were 808.87 ms and
814.60 ms respectively. This supports no material allocation regression in this
workload, not a general performance guarantee. Mapping expansion without
recalculation allocated 11.15 / 89.09 / 255.00 MiB for 1k / 10k / 30k rows;
with recalculation it allocated 22.56 / 199.25 / 579.35 MiB. Both paths checked all
formula cells. The 100,000-cell SUM returned exactly 200,000, at 12.75 ms and
14.97 MiB allocated. All 30,000 independent/common-dependent formulas returned
22, with checksums 660,000. Bounded session caching avoids repeatedly evaluating
the shared dependency; no per-cell independent recalculation loop is used.

Time scaling in this shared-container run is noisy (including concurrent
compilation). A quiet repeat is needed before interpreting the large-case timing
ratios. The recorded allocations increase roughly with workload size, and no
unbounded full-column materialization is part of these cases. Peak working-set
values include process history and cannot be subtracted as incremental costs.
Excel desktop was not used for these checks.


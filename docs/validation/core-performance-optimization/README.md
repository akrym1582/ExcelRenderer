# Core performance evidence — 2026-11-11

See [implementation, results and limits](../../core-performance-optimization.md).

- `baseline-commit.txt`, `plan-baseline-delta.txt`: actual pre-change revision and differences from the supplied plan.
- `phase1.patch` … `phase5.patch`: cumulative product patches; apply one patch to the baseline, not all patches in sequence.
- `common-harness.patch`: apply to each rebuilt comparison runtime. Primary allocations/GC exclude PDF validation.
- `environment.json`, `source-state.json`: input/font/DLL hashes, runtime settings, CPU/OS/SDK, source hashes and final binary identity.
- `inputs/`, `normalized-inputs/`: the exact generated/normalized bytes shared by before and after.
- `samples.jsonl`, `summary.json`: primary Core cold/warm samples and per-round medians; observer disabled.
- `main-samples.json`: separate main PDF/PNG/SVG primary protocol; existing observer enabled on both versions.
- `diagnostics.json`: separate per-phase observations; never substituted into primary timing ratios.
- `controlled-samples.jsonl`, `regression-rechecks.jsonl`, `analysis.json`: all follow-up samples and round-level ratios. Follow-ups disable tiering and collect eight warm conversions. The 5% time/RSS goal remains unconfirmed for small cases; no pooled-median pass is claimed.
- `baseline-diagnostics.patch`, `baseline-observations.json`: an observation-only baseline variant for glyph/Paint/array/index counts. Its output matches the uninstrumented baseline; its timings are excluded from performance comparisons.
- `structural-validation.json`: checked command build, font run, cache, payload and resource assertions.
- `output-comparison.json`: identical raster/text/geometry/subsets for Core/main and identical native PNG/SVG files. Generated PDFs remain in `artifacts/core-performance/`.
- `*-api-*.txt`, `api-comparison.json`: repository semantic public/protected/default/nullable snapshots.
- `*.log`, `coverage.cobertura.xml`, `reader-isolation.json`: executed build/test/static/isolation checks.
- `artifact-hashes.json`: SHA-256 for retained files, excluding the manifest itself. Regenerate last with `python3 scripts/summarize-core-optimization.py`.

All builds/tests/render processes were sequential. Counter-only and tiering-disabled results are separate protocols. Windows was not executed. No frozen historical PDFs or historical reports were rewritten.

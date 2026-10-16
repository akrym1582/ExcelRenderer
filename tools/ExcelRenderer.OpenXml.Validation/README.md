# Open XML reader validation

Run from the repository root with .NET 10. Only use controlled fixtures; this tool does not implement upload/ZIP validation.

```bash
dotnet restore ExcelRenderer.slnx
dotnet run --project tools/ExcelRenderer.OpenXml.Validation -c Release -- compare --repository-root . --output artifacts/openxml/compare.json
dotnet run --project tools/ExcelRenderer.OpenXml.Validation -c Release -- generate --output artifacts/openxml/fixtures
dotnet run --project tools/ExcelRenderer.OpenXml.Validation -c Release -- benchmark --input artifacts/openxml/fixtures --output artifacts/openxml/benchmark.json
```

`compare` reads the retained parent-revision projections in
`docs/validation/closedxml-removal/raw-reader-baseline.json`. It verifies input
SHA-256 and the complete source-neutral workbook, worksheet, cell, style and
display snapshots for all recorded samples. Every mismatch or read failure exits
nonzero. The original comparison against the old engine remains historical
evidence in `docs/validation/closedxml-removal/baseline-reader-comparison.json`;
the current executable has no dependency on that engine. Baselines must be
generated from the original revision, never silently replaced with current output.

`generate` uses `OpenXmlWriter` to create six cases: 100,000 numeric cells, 100,000 repeated shared strings (10 entries), 100,000 unique shared strings, 100,000 styled empty cells, two cells at opposite grid corners, and a 100,000-cell unselected sheet plus 10 selected cells. Fixture generation is excluded from measurements.

`benchmark` uses a new process for **every** reader, warmup and repetition: one discarded warmup and five samples per reader/case/projection. It reports medians and all samples, input SHA-256, source identity, OS/SDK/runtime/culture, cell count, deterministic address/text checksum, allocation, GC counts and peak RSS. The OpenXml worker consumes and formats cells without retaining a cell projection. `parse-only` is a separate projection. Counts/checksums must be deterministic. The time ratio never controls exit status; worker failures or projection mismatch do.

Open/styles time, metadata pass, enumeration time and shared-string load time are reported separately. Shared-string time is **included in enumeration** and must not be added a second time. Historical engines must be measured from their original checkout. The reader exposes loaded SST count, style count and SST load time for this purpose. Peak RSS also includes runtime/JIT/package startup and is measured independently per worker; median elapsed time includes each reader's first-use work but not process launch or fixture hashing. These are process samples on this host, not a stable CI speed guarantee.

The JSON `Environment` records the base commit and a dirty-worktree marker rather than pretending uncommitted changes have a commit SHA. `ValidationSourceSha256` hashes sorted relative path + NUL + contents + NUL for files under the three new project directories (excluding bin/obj), plus `Directory.Build.props`, `ExcelRenderer.slnx` and the dependency check script. This identifies the exact code/test/tool snapshot without creating a commit.

For follow-up optimization, rerun the same benchmark from a detached worktree at the prior commit and from the updated working tree, using the **same absolute fixture directory** and different output paths. Build both versions first; then run regression tests and both benchmarks sequentially to avoid CPU contention or rebuilds deleting test fixtures. Compare input hashes, cell counts and checksums across versions, and retain both provenance records. See [the optimization report](../../docs/openxml-reader-optimization.md) for the measured before/after results and the adapter API changes.

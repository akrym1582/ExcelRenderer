# Open XML reader validation

Run from the repository root with .NET 10. Only use controlled fixtures; this tool does not implement upload/ZIP validation.

```bash
dotnet restore ExcelRenderer.slnx
dotnet run --project tools/ExcelRenderer.OpenXml.Validation -c Release -- compare --repository-root . --output artifacts/openxml/compare.json
dotnet run --project tools/ExcelRenderer.OpenXml.Validation -c Release -- generate --output artifacts/openxml/fixtures
dotnet run --project tools/ExcelRenderer.OpenXml.Validation -c Release -- benchmark --input artifacts/openxml/fixtures --output artifacts/openxml/benchmark.json
```

`compare` recursively discovers XLSX fixtures under `tests/ExcelRenderer.Tests/SampleInputs` and `samples`. It compares XML-present cell addresses/counts, raw values, typed values, display text, font/fill/border/alignment/color metadata, merges, row/column dimensions, print areas, scale/fit width and margins. Merges are compared as sets. Column widths are compared in saved units: ClosedXML 0.105.1 subtracts `XLConstants.ColumnWidthOffset` (0.710625), so the tool adds it back. It does not compare `CellsUsed` counts to XML cell counts. Formula presence/raw text/cache text are checked against the original ZIP XML; it never calls formula `Value`/`GetFormattedString`. Synthetic tests separately verify formula display and group membership, print titles, advanced metadata, style inheritance, theme/tint and resource contracts. Formula display is not cross-checked by triggering ClosedXML recalculation.

Every unexplained difference or read failure exits nonzero. Only two narrow display explanations are recognized: unsupported number formats that explicitly emitted `UnsupportedNumberFormat`, and built-in date format 14's explicit `mm-dd-yy` versus ClosedXML localization. Explanations are emitted per difference with field/address/code/reason; they are not a fixture-wide allowlist. Missing formula caches and unresolved colors remain diagnostics. No differences were waived in the recorded repository run.

`generate` uses `OpenXmlWriter` to create six cases: 100,000 numeric cells, 100,000 repeated shared strings (10 entries), 100,000 unique shared strings, 100,000 styled empty cells, two cells at opposite grid corners, and a 100,000-cell unselected sheet plus 10 selected cells. Fixture generation is excluded from measurements.

`benchmark` uses a new process for **every** reader, warmup and repetition: one discarded warmup and five samples per reader/case/projection. It reports medians and all samples, input SHA-256, source identity, OS/SDK/runtime/culture, cell count, deterministic address/text checksum, allocation, GC counts and peak RSS. Both readers consume and format cells without retaining a cell projection. `parse-only` is a separate projection. Counts/checksums must match across readers. The time ratio never controls exit status; worker failures or projection mismatch do.

Open/styles time, metadata pass, enumeration time and shared-string load time are reported separately. Shared-string time is **included in enumeration** and must not be added a second time. ClosedXML's workbook load is reported as open time; it cannot expose matching internal stages here. The reader exposes loaded SST count, style count and SST load time for this purpose. Peak RSS also includes runtime/JIT/package startup and is measured independently per worker; median elapsed time includes each reader's first-use work but not process launch or fixture hashing. These are process samples on this host, not a stable CI speed guarantee.

The JSON `Environment` records the base commit and a dirty-worktree marker rather than pretending uncommitted changes have a commit SHA. `ValidationSourceSha256` hashes sorted relative path + NUL + contents + NUL for files under the three new project directories (excluding bin/obj), plus `Directory.Build.props`, `ExcelRenderer.slnx` and the dependency check script. This identifies the exact code/test/tool snapshot without creating a commit.

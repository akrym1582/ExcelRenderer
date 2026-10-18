# Follow-up rendering evidence

See [the review](../rendering-performance-review.md) for scope, interpretation and limitations.
Before is the removal implementation at `1e1f509` / product `0996eb0`; after is `104d685`.
The same newly built performance harness runs with each set of unchanged product DLLs.

- `conditions.json` / `implementation.json`: exact DLL/source/input hashes, rounds and ordering.
- `summary.json`: all 14 conditions, time/allocation/RSS medians and maxima.
- `samples.jsonl`: all 240 complete measured records, including phase/resource counts,
  snapshots and layout/direct-image fingerprints; `samples.csv` projects scalars/metrics for analysis.
- `comparison.svg` / `.png`: time and allocation ratios against the removal implementation.
- `stage-b-rich*.json` / `stage-b-rich-samples.jsonl`: separate five-round check against
  the historical stage-B implementation; ten measured process records. No raw outlier was removed.
- `retention.json` / `retention.csv`: final 100-conversion resource totals and all post-GC observations.
- `api-diff.json` / `core-api-diff.json`: zero public signature additions/removals in this follow-up.
- `package-inventory.json` / `consumer-graphs.json`: four fresh packages and nine isolated consumers.
- `font-formats.json`: six independent Unicode extraction probes after the changes.
- `verification.json` / `ci.json`: final local verification and commit-specific remote job status.

Frozen A/B PDFs, fonts, manifests and golden images remain unchanged. Current comparisons
under `artifacts/pdf-review/comparison` report 25 A→C fixtures with only the existing 51
reviews, and zero differences/exceptions for all 25 B→C fixtures. The CI artifacts contain
reproducible detailed PDFs/rasters without committing large generated documents.

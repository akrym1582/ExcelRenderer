# Rendering review evidence

See [the report](../../rendering-review-fixes.md) and [Japanese report](../../rendering-review-fixes.ja.md).
The base is 1.10.5 / `df74eb0`; `a71e21d` is the separate historical baseline.

- `source-state.json` records changed product source hashes and the saved-runtime stages.
  `clock-only.patch`, `preflight.patch`, `cache-roles.patch` apply sequentially to the base.
  Reverse-apply checks also pass against the final source. No benchmark switches were added to product code.
- `samples.jsonl`, `environments.jsonl`, `environment.json`, `summary.json` retain the
  22-case primary comparison, including cold/warm samples and every process DLL/input/font hash.
  JSON array records from the existing runner are stored as JSONL without omitting records.
- `controlled-samples.jsonl` and `regression-analysis.json` retain the separate tiering-disabled
  follow-up of every primary time/RSS flag. Controlled results do not replace primary observations.
- `clock-comparison-*`, `cache-roles-*`, `historical-*` retain each focused comparison,
  including full samples, process environments and per-round medians. Their protocol differs from
  the broad primary comparison and is documented separately.
- `layout-samples.jsonl` / `layout-summary.json` retain all nine exact layout comparisons.
- `diagnostics.jsonl` is the separate observer-enabled capture run; `structural-observations.json`
  extracts page, cache and native lifetime counts. `output-quality.json` compares the captures;
  `api-comparison.json` is the metadata signature audit. Capture artifacts and saved runtimes remain
  under `artifacts/rendering-review/`; font subset hashes and canonical output hashes are retained here.
- `cases.json` records the specifications used in this checkout. Re-run `--prepare` to resolve
  paths for another checkout; copy the retained `range-source.xlsx` first to reuse its exact bytes.
  All other inputs already exist in the repository, identified by the input hashes.
- `checks.json` and `logs/` retain executed build/test/coverage/ownership/dependency/isolation checks.
  No package or dependency changes require a new package layout validation.
- `artifact-hashes.json` covers every retained evidence file except itself.

Build, test and performance activity were sequential. RSS is a process lifetime high-water mark;
neither it nor logical cache retention units establish a byte-accurate per-conversion memory limit.
Some timing/RSS flags remain; the report preserves them rather than asserting universal 5% acceptance.
Local execution was Linux only.

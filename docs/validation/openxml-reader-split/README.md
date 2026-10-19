# Reader split validation evidence

Base: fetched main `283dc5b64d4ad54f640d3c6d179f0fff2b0631b1`; branch `refactor/openxml-reader-split`. The implementation commit is the commit containing this report. Linux x64 / Debian 13, .NET SDK 10.0.401, runtime 10.0.12. No release or push was performed.

See [the migration, reproduction commands and result tables](../../openxml-reader-split.md).

- `checks.json`: executed checks, counts, environment and explicit limitations.
- `isolation.json`: published Core-only/Reader-only DLL inventory, restore/runtime graph and PE AssemblyRefs. `isolated-pdf-text.log` records independent extraction of saved results.
- `packages.json` and `package-consumer-graphs.json`: four-package inventory, shared DLL/XML hashes and independently restored consumers.
- `performance-summary.json` / `performance-samples.json`: formal three-round, ten-warm-up/five-measured default-runtime comparison; source/fixture/product hashes, exact values, allocations, RSS and existing phase/read counters.
- `performance-one-warmup-*`: initial high-variance run, retained separately rather than treated as final steady-state evidence.
- `oracle-*.json`: 1,508 specification/profile matches, no mismatches; strict Excel comparison remains incomplete because the existing observation file is empty. Detailed transient reports are in artifacts/reader-oracle.
- Other `.log` files: builds, suites, namespace/dependency audits, package/CLI/font checks, independent PDF/image comparisons and mutation detection. A failure shown in a mutation log is intentional and must be detected before source restoration.

Windows execution and live Microsoft Excel observations were unavailable. The historical 1.9.0/1.9.1 sample is normalized back to its exact original source by the historical verifier; current namespace samples were run with the newly generated packages. Existing frozen PDFs, font assets and prior validation evidence were not changed.

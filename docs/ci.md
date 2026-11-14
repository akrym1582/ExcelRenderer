# CI validation ownership

The `Unit tests` workflow runs on every pull request and on manual dispatch,
using Linux only. Windows validation is deferred to the publish workflow.

| Job | Responsibility |
| --- | --- |
| Run unit tests | Main renderer, OpenXml and Mapping suites; namespace, dependency-direction and publication-allowlist checks; PostScript validation |
| Core (Linux) | Core suite in a sparse checkout without shared fixtures or OS font setup; coverage and separate uninstrumented performance checks |
| Formulas (Linux) | Isolated calculation suite, dependency audit and formula oracle |
| PDF rendering regression | Frozen A/B/Core baselines, visual parity, reviewed PDFs, borders, pagination, images, font extraction and PDF-comparator mutation checks |
| pdf-distribution (Linux) | CLI suite, pack/publish, dependency and package inventory audits, isolated package consumers; managed-font and optional-font validation |

Rendering waits for the main and Core jobs and downloads their existing artifacts.
Both artifacts preserve repository-relative paths, including `tests/.../SampleOutputs`.
The rendering job builds its own tools before downloading the generated fixtures;
it does not rerun the suites to regenerate them. Distribution runs independently,
so a rendering comparison failure cannot skip package validation.

The reusable distribution workflow defaults to running its Core and selected main
renderer tests on Linux and Windows. The publish workflow retains this default,
including the Windows solution build and package validation before publishing.
The PR workflow selects Linux only and opts out of Core tests and Linux main
tests already owned by dedicated jobs. Main/Core semantic parity remains covered
by the main Linux suite; the separate Windows parity job is disabled. On Linux PR runs, the two fixture-producing
font tests still run locally: independent managed-font validation requires their
`PdfFormats/colr*.pdf` and `ManagedTrueType/collection-*.pdf` outputs. This keeps
distribution independent of the main job without rerunning its broad test suite. The font-split push workflow uses the same
validation implementation with a Linux-only matrix.

## Source mutation checks

`source-mutations.yml` runs the expensive source-rewriting checks in a separate
checkout on relevant source/test/configuration changes, weekly, and on manual
dispatch. Its path list must be updated whenever a mutation targets a new source
file. Weekly/manual execution covers indirect dependencies outside that list.
Do not require this path-filtered workflow as an unconditional branch-protection
check: unrelated PRs intentionally do not run it.

The regular rendering and distribution jobs retain their PDF and dependency-audit
mutation checks. Frozen baselines and comparison tolerances are unchanged.

Workflow changes should be checked for YAML/shell syntax, artifact producer and
consumer paths, and complete suite ownership. Actual elapsed time and artifact
transfer behavior must be verified in GitHub Actions; moving jobs alone does not
guarantee a proportional reduction in total runner time.

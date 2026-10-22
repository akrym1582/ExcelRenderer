# Main/Core direct parity and usage validation

Base: `aa1b8006de4b20e449ad4a015c70928c617858a2` (latest origin/main fetched at task start, 2026-10-22).
Branch: `codex/main-core-parity-usage`. No pre-existing working-tree changes.

This report concerns the common single-regular-font/saved-value contract, not complete Excel
compatibility. Product code, public APIs, package layout, independent Core dependencies and
frozen reference PDFs were not changed. No independently demonstrated product defect required
a product fix. Nonpainting image clip-path differences are normalized only in the PdfPig
paint comparison; the existing Linux raster gate still compares their actual effects.

19 synthetic common cases, 5 new attribute-specific intended-difference cases and 1 default
sheet-selection contract are implemented. Existing Core regular-font, transformed/start-page
image/shape and saved-formula tests and main recalculation tests remain applicable (linked in
the comparison guide). New default selection evidence confirms both current APIs include hidden
worksheets. No broad expected-difference masks or tolerance increases are used.

Commands (run sequentially when they share output):

```sh
dotnet restore ExcelRenderer.slnx
dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
dotnet test ExcelRenderer.slnx -m:1 --logger 'trx;LogFileName=solution.trx' --results-directory artifacts/main-core-parity/solution
python3 scripts/check-main-core-parity.py --run tests/ExcelRenderer.Tests/bin/Debug/net10.0/SampleOutputs/MainCoreParity
python3 scripts/check-source-namespaces.py
python3 scripts/check-no-pdfsharp.py --configuration Debug
```

Command results and environment are retained in [results.json](results.json); per-case
font/input hashes and independent raster results are retained in [case-results.json](case-results.json). Large PDFs and difference rasters belong in ignored artifacts/CI
artifacts, not source. The existing 44 reviewed PDF baselines are never regenerated.

Windows execution is configured in CI, not emulated on Linux. Core product code and project/
distribution files are unchanged, so conditional Core coverage/performance and pack/isolation
requirements do not arise from an implementation change; any extra checks actually run are
listed below. Final published-package consumption is distinct from repository ProjectReference
samples. This work does not publish a package or merge the PR.


Final checks use Release outputs:

```sh
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
dotnet test ExcelRenderer.slnx -c Release --no-build -m:1 --logger trx --results-directory artifacts/main-core-parity/final-solution
python3 scripts/check-main-core-parity.py
python3 scripts/check-libreoffice-regression.py
python3 scripts/audit-libreoffice-borders.py --run artifacts/libreoffice-regression --output artifacts/libreoffice-regression/border-audit.json
python3 scripts/check-no-pdfsharp.py
python3 scripts/check-core-dependencies.py
python3 scripts/check-source-namespaces.py
python3 scripts/package_distribution.py
```

The reviewed-output gate passes 44 PDFs and detects all seven mutations, including an exact
same-coordinate duplicate border. The separate border audit verifies five worksheets and 23
physical strokes per product without duplicate strokes. The dependency audit checks 24 restored
projects and 726 managed AssemblyRef inventories. Release rebuild reports zero warnings/errors.
The original validation at commit `857f745` covered 61 assertion/stream/HTTP tests and
2,940 solution tests. These historical counts precede the scope reduction below.
Expected-difference and default-selection pairs now retain their PDFs, exact XLSX and hash JSON
as well; assertion status is in TRX. Existing Core scope tests supplement the five new difference
cases without introducing a main/shared dependency into Core tests.

All eight product DLL hashes used by the 44-PDF gate remain identical after the final test-only
evidence-retention change. No prior reviewed PDFs, inputs or validation reports were changed.

Live sample verification: both `dotnet run --no-build -c Release` file commands produced a
readable one-page PDF; all four hosted routes returned 200 application/pdf and a readable
one-page PDF. Exact executed commands and results are in results.json. The Web SDK changes
the child's working directory, so the root-relative README commands were corrected to pass
absolute `$PWD` paths. The destination directory must exist. See the bilingual sample README
for the exact startup commands and direct/validated error contracts.

Windows runs are configured in PR CI and were not executed on this Linux host. No package was
published; pack and standalone distribution gates are not applicable because no project or
package composition changed. Core's 413 tests passed in the complete solution, while its
separate coverage/performance gate is unchanged and not newly required by test/sample changes.


## Follow-up: remove HTTP test additions

At the user's request, the HTTP sample improvements remain, while the newly added
ValidatedPdfHttpTests and the additions to AsyncPdfStreamTests are removed. The existing
AsyncPdfStreamTests is restored exactly to the starting main revision. The Windows comparison
job runs only parity, diagnostic-policy and file-publication tests; it no longer adds HTTP
coverage. Guides describe this reduced scope. The earlier results.json and live sample records
remain historical evidence for commit 857f745; current follow-up results are in
[http-test-scope-reduction.json](http-test-scope-reduction.json).

Follow-up verification: Release rebuild has zero warnings/errors. All 2,919 solution tests pass,
including the 29 cases retained in the Windows comparison filter. No HTTP sample code changed.

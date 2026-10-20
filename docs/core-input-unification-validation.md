# Core input settings unification: validation

Base: fetched main `48051fd27b9431fbd0f2adbecf65fa50e4b013ee`. Branch:
`refactor/core-reader-input-unification`. Validation runs on Linux x64 with .NET SDK
10.0.401. Historical validation reports retain their original results.

Final integration takes main `d5fa038cc1e097900a4dcbc5c09723c584e63edd`, which
only updates the four published package versions from 1.10.1 to 1.10.2. All 2,652
regression tests and the first distribution checks passed before that metadata
update. The implementation sources are identical after rebasing. Restore,
Release Rebuild, input contract tests and distribution validation are repeated
for 1.10.2 below.

## Change and migration

Removed `src/ExcelRenderer.Core/WorkbookInputOptions.cs` and
`src/ExcelRenderer.Core/Input/WorkbookInputPreparer.cs`. Core's `PdfExportOptions.Input`
now exposes the existing `ExcelRenderer.OpenXml.Reader.Input.WorkbookInputOptions`,
and `ExcelConverter` passes that instance directly to Reader preparation, with one
`buffer.spillCount` callback. No compatibility type, replacement wrapper, algorithm
copy, linked source or new package was added. Reader code and project references
are unchanged. Core's PDF/output buffer factory remains in Core.

This removes a public Core type and changes the property signature. Rebuild
consumers, use Reader.Input for explicit input type names, and replace input-record
`with` expressions with new Reader settings. Reader options remain a mutable sealed
class with setters. `PdfExportOptions` stays a record and Input stays init/new(), but
record copies share Input unless replaced. Do not mutate shared input settings
during conversions. See [English migration](core.md#input-settings-migration) and
[日本語の移行説明](core.ja.md#入力設定の移行).

The six Reader defaults remain 16 MiB memory, 128 MiB input, temporary files
disabled, null temporary directory, 10,000 ZIP entries and 512 MiB expanded data.
Main retains its public input record and preparer; Mapping, Tool and OpenXml public
APIs retain their existing contracts.

## Regression coverage

Core's existing converter tests now use Reader settings for input byte, memory,
ZIP entry and expanded-size limits. The API contract checks the actual Input
PropertyType and absence of the old exported type. Converter tests check a real
spill in the requested directory and cleanup after success, ZIP validation
failure, downstream sheet-selection failure and cancellation after the file is
created. They also check caller streams stay open, failed output stays empty,
exactly one input spill notification and no notification for an in-memory input
with the same small PDF fixture. New observers restore the previous value in
finally. Existing seekable/nonseekable current-position tests remain in place.

Reader/main boundary tests and their independent spool-reader checks are retained;
Core converter coverage does not depend on a test-only direct Reader call.
Structure checks reject restoration of either removed file or either Core input
type, require direct Reader settings/preparation and retain the existing buffer
factory, dependency-direction and Reader independence checks. The package-only
main/Core consumer compiles explicit Reader settings and checks the migrated API
before conversion.

## Execution

Build, test, isolation and pack commands sharing output directories run sequentially.
`source scripts/test-fonts.sh` is used for test and rendering validation. Commands
that need restore or test-host sockets run with the environment's network access.

| Check | Result |
| --- | --- |
| `dotnet restore ExcelRenderer.slnx` | Passed |
| `dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal -m:1` | Passed; 0 warnings, 0 errors |
| `dotnet test ExcelRenderer.slnx --no-restore -m:1` | Passed; 2,652 tests, 0 failed, 0 skipped |
| `python3 scripts/check-core-dependencies.py` | Passed |
| `python3 scripts/check-source-namespaces.py` | Passed; 685 namespaces, 2 required shims |
| Release Rebuild after the 1.10.2 metadata update | Passed; 0 warnings, 0 errors |
| Core ContractsTests and main InputBufferBoundaryTests after the metadata update | Passed; 31 tests, 0 failed, 0 skipped |
| `python3 scripts/check-no-pdfsharp.py --packages artifacts/core-input-unification/packages-1.10.2` | Passed; 1,258 source/config files, 23 restored projects, 778 managed AssemblyRef inventories, 4 packages |
| `python3 scripts/validate-reader-isolation.py` | Passed; independent Reader/Core restore, build, publish, graph, AssemblyRef and execution checks |
| Release build, four-package pack and `package_distribution.py` | Passed; build has 0 warnings/errors; exactly 4 packages, no unpublished dependencies, matching shared DLL/XML hashes |
| Isolated package consumers | Passed; 11 consumers, installed CLI collection-face selection and expected unsupported-font rejection |
| Optional font package consumers | Passed; independent consumers with/without Fonts and matching optional/Tool Fonts DLL |
| Source search, local documentation links and `git diff --check` | Passed; old type names occur only in rejection guards; removed preparer references are absent |

Suite totals: Core 344, main 972, Formulas 833, Mapping 172, OpenXml 213 and
Tool 118. The main run includes the available Ghostscript image comparisons.
The Ghostscript comparison code and tolerances are unchanged.

The installed CLI emitted the existing MaximumDigitWidthFallback runtime
diagnostic for the style-free fixture; generated outputs and CLI checks passed.

The 1.10.1 distribution evidence is retained with the initial outputs. The 1.10.2
package inventory, dependency audit and independent Reader/Core results are under
`artifacts/core-input-unification/*-1.10.2.json`; raw command logs are retained in
`artifacts/core-input-unification/logs/`. New source consumers and package caches
use separate directories.

For the final distribution checks, use fresh package/consumer directories:

```bash
dotnet restore ExcelRenderer.slnx
dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
dotnet test tests/ExcelRenderer.Core.Tests/ExcelRenderer.Core.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ContractsTests'
dotnet test tests/ExcelRenderer.Tests/ExcelRenderer.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~InputBufferBoundaryTests'
python3 scripts/validate-reader-isolation.py --work artifacts/core-input-unification/reader-isolation-1.10.2 --output artifacts/core-input-unification/reader-isolation-1.10.2.json
dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -p:BuildProjectReferences=false -o artifacts/core-input-unification/packages-1.10.2
python3 scripts/package_distribution.py --packages artifacts/core-input-unification/packages-1.10.2 --output artifacts/core-input-unification/package-inventory-1.10.2.json
python3 scripts/check-no-pdfsharp.py --packages artifacts/core-input-unification/packages-1.10.2 --output artifacts/core-input-unification/dependency-audit-1.10.2.json
python3 scripts/validate-formulas-packages.py --packages artifacts/core-input-unification/packages-1.10.2 --work artifacts/core-input-unification/package-consumers-1.10.2
python3 scripts/validate-font-packages.py --packages artifacts/core-input-unification/packages-1.10.2 --work /tmp/excelrenderer-core-input-font-consumers-1.10.2
```

No Windows execution is claimed. No release version, NuGet publication or merge
is part of this change.

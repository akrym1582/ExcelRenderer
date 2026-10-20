# AI coding agent guide

## Repository

- `src/ExcelRenderer` is the .NET Standard 2.1 library. `src/ExcelRenderer.Fonts` is the optional font package. `src/ExcelRenderer.Tool` is the .NET 10 CLI. Their tests are under `tests`.
- The rendering flow is `ExcelReader` → `ReportDocument` (`Model`) → `ReportLayoutEngine` (`Layout`) → `DrawCommandGeneratorPass` (`Drawing`) → `PdfRenderer`, `PngRenderer`, or `SvgRenderer`. Markdown export consumes `ReportDocument` through a separate path.
- Keep parsing, layout, drawing commands, and output rendering in their respective layers. Add layout behavior as an `IReportLayoutPass` and place it deliberately in `ReportLayoutEngine`.

## Maintaining the main library and Core

- Only ExcelRenderer, ExcelRenderer.Mapping, ExcelRenderer.Tool and ExcelRenderer.Fonts publish NuGet packages. Keep all other projects nonpackable. Main and Mapping bundle identical Reader/OpenXml/Formulas DLL/XML files via `build/EmbeddedOpenXml.targets`; Tool includes one copy of each assembly. Run `scripts/package_distribution.py` and isolated package consumers after distribution changes. See [the distribution contract](docs/package-distribution.md).
- `src/ExcelRenderer.Core` is the renamed PDF-only foundation. `ExcelRenderer` references Core; Core references only OpenXml.Reader; it must never reference OpenXml, Formulas, the main library, Fonts, or Tool. Keep Core nonpackable and ship its DLL/XML in the existing main package. Tool must include the same Core DLL.
- Workbook input validation and generic seekable spool algorithms belong to `ExcelRenderer.OpenXml.Reader/Input`. Core and main use Reader owners and buffers directly. Core PdfExportOptions.Input uses Reader input settings, and ExcelConverter calls Reader preparation directly with one conversion-local spill callback. Main retains its public input settings and copies them in its input preparer. Core InputBufferFactory/main RenderBufferFactory create buffers with conversion-local spill metrics. Reader must not reference other product projects, rendering or font libraries. OpenXml references Reader and the BCL-only Formulas engine for editing/recalculation. Main and Mapping use that adapter; Core reads saved values through Reader.
- Fix shared image-resource, column-width, axis/band, placement, border, style interning/reading, common workbook/cell traversal, sheet/page layout, cell-layer drawing, diagnostic aggregation, and PDF drawing/saving algorithms in Core. Keep public main-library models and APIs in their existing assembly, using explicit adapters at the boundary.
- CoreExcelReader owns shared traversal and a read-local generic cell value resolver. FullExcelReader owns recalculation and its selection policy; build its snapshot before streaming, and substitute values before style/display conversion. Cached reads need no calculation snapshot unless enabled conditional formatting requires one.
- Main's enabled conditional formatting may lazily create a saved-value snapshot even in CachedValue mode; reuse the display session in Recalculate mode. Keep no-CF, disabled-CF and Core reads free of extra snapshots. Main owns conditional populations, typed comparisons, thresholds and dynamic fills; Reader supplies immutable metadata/colors, and OpenXml supplies sparse coordinates on the shared snapshot. See [the conditional-formatting contract](docs/conditional-formatting.md).
- For population rules, normalize the whole source sqref union, including hidden and selected-out values. Coalesce adjacent Mapping-expanded ranges before the bounded union sweep; never enumerate missing whole-sheet coordinates or rebuild/sort a population for each target cell. Retain independently specified Mapping fill counts when diagnosing resource-limit failures.
- Consult [the source ownership table](docs/core-source-map.md). Basic reader, geometry, page planning/building, cell-layer command generation and PDF operations belong to Core. FullLayoutPolicy/FullDrawingExtension and the main PDF adapters supply product differences; do not restore algorithm copies in public facades.
- Preserve [Core's supported scope](docs/core.md): one supplied regular font, variation selectors reduced to base characters, PDF only, no shapes, link annotations, range/page selection, or transformed/split images. Preserve the main library's complete feature set.
- Run both regression suites for shared changes. Native font, subset and drawing resources belong to each conversion. Internal measurement/preflight borrows its scope; independent public measurement/rendering owns and restores its scope. Never add a global conversion lock or retain native handles in public layout results.
- PDFsharp is prohibited in source, restore graphs and distributions; run `scripts/check-no-pdfsharp.py` after restoring/building all projects. Keep frozen historical PDFs and their hashes unchanged.
- Keep source ownership, English/Japanese usage guides and validation reports accurate. Historical validation is evidence only for its recorded commit and contract.

## Changes

- Match declared namespaces to project roots and source folders. Keep partial declarations together. Run `python3 scripts/check-source-namespaces.py`; only the two compiler-required IsExternalInit shims are exempt. See [namespace migration](docs/namespace-migration.md).

- Coordinates and dimensions in the model and drawing commands are PDF points. Convert to pixels only at PNG output using the requested DPI.
- Merged ranges use their top-left cell for `RowSpan` and `ColumnSpan`; avoid drawing the remaining cells again.
- When a drawing command or style changes, check PDF, PNG and SVG outputs through the shared Skia primitives. Preserve Excel line style and width separately when converting borders.
- Japanese fonts are embedded in the optional `ExcelRenderer.Fonts` package. The `ExcelRenderer` library does not depend on that package, while the CLI depends on it transitively. Keep README.md and README.ja.md consistent about package responsibilities, and keep font licenses and `THIRD-PARTY-NOTICES.md` aligned with any font changes.
- Optional embedded fonts are discovered from DLLs beside `ExcelRenderer.dll`; do not depend on a particular font-package DLL name, and do not let an unloadable or invalid neighboring DLL abort discovery. If no matching resource exists, loose font files are resolved from the `ExcelRenderer.dll` directory tree.
- Preserve nullable annotations and consider existing callers before changing public APIs. Follow the surrounding C# style and the language used in each file; `README.md` is English and `README.ja.md` is Japanese.
- Document only behavior that is implemented. Keep changes focused and add meaningful tests for changed behavior in the relevant test project.

## Code quality

- Treat every StyleCop diagnostic as required work. Do not finish a change with StyleCop warnings, and do not silence them with `NoWarn`, pragmas, or broad analyzer exclusions merely to make the build pass; fix the code or document a narrowly scoped, technically necessary exception.
- Preserve the existing StyleCop conventions when adding or moving code, including member ordering, braces, blank lines, multi-line argument layout, trailing commas, expression precedence, and XML documentation for documented APIs.
- Analyzer output can be hidden by incremental builds. After editing C# or analyzer configuration, run a rebuild and confirm that it reports zero warnings. If an automated formatter is used, inspect its complete diff and remove unrelated or low-quality rewrites before committing.

## Verification

- From the repository root, run `dotnet build ExcelRenderer.slnx -t:Rebuild --no-restore --verbosity:minimal` after code changes and require zero warnings and zero errors. Run `dotnet restore ExcelRenderer.slnx` first when assets are unavailable.
- From the repository root, run `dotnet test ExcelRenderer.slnx` after code changes.
- For Core changes, also run its CI coverage gate: `dotnet test tests/ExcelRenderer.Core.Tests/ExcelRenderer.Core.Tests.csproj -c Release --no-build --filter "Category!=Performance" -p:CollectCoverage=true`, followed by the same command with `--filter "Category=Performance"` and without `CollectCoverage`. Normal solution tests do not enable this gate. Preserve Core's independent fixture/font setup.
- On Linux, first run `source scripts/test-fonts.sh` in the same shell as `dotnet test` to isolate Fontconfig to the committed test assets. Run build, rebuild, test and pack sequentially when they share output directories; a concurrent rebuild can remove assemblies used by CLI integration tests. Use `-m:1` when memory is limited.
- When package contents or project files change, also verify the affected package with `dotnet pack`. Run `python3 scripts/validate-reader-isolation.py` for standalone Reader/Core restore, output, AssemblyRef and execution checks. This generates temporary consumers; do not add standalone product packages.
- Before isolated package consumers or benchmark worktrees, check free space in both the workspace and temporary filesystem. Keep large baseline Release runtimes under workspace `artifacts/` when temporary space is limited; remove only task-created obsolete consumers/worktrees after retaining required evidence.
- Benchmark saved before/after Release runtimes without overlapping builds, tests or rendering. Retain raw samples, DLL/input hashes, warm-ups and per-round results. Reuse fixture bytes with `--fixture-source` for Phase 2 follow-ups; do not claim a 5% regression target from a noisy pooled median. See [the Phase 2 protocol and limits](docs/validation/conditional-formatting-phase2/README.md).
- Documentation-only follow-ups need link, diff and retained-artifact hash checks rather than another full build/test run. Keep README.md/README.ja.md and current contracts aligned; preserve historical validation claims and update `artifact-hashes.json` whenever retained evidence files change.

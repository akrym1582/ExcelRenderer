# AI coding agent guide

## Repository

- `src/ExcelRenderer` is the .NET Standard 2.1 library. `src/ExcelRenderer.Fonts` is the optional font package. `src/ExcelRenderer.Tool` is the .NET 10 CLI. Their tests are under `tests`.
- The rendering flow is `ExcelReader` → `ReportDocument` (`Model`) → `ReportLayoutEngine` (`Layout`) → `DrawCommandGeneratorPass` (`Drawing`) → `PdfSharpRenderer` or `PngRenderer`. Markdown export consumes `ReportDocument` through a separate path.
- Keep parsing, layout, drawing commands, and output rendering in their respective layers. Add layout behavior as an `IReportLayoutPass` and place it deliberately in `ReportLayoutEngine`.

## Changes

- Coordinates and dimensions in the model and drawing commands are PDF points. Convert to pixels only at PNG output using the requested DPI.
- Merged ranges use their top-left cell for `RowSpan` and `ColumnSpan`; avoid drawing the remaining cells again.
- When a drawing command or style changes, check both PDFsharp and SkiaSharp renderers. Preserve Excel line style and width separately when converting borders.
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
- When package contents or project files change, also verify the affected package with `dotnet pack`.

#!/usr/bin/env python3
"""Export the investigation commit and overlay instrumentation without optimizations."""
import argparse
import io
import pathlib
import shutil
import subprocess
import tarfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("destination", type=pathlib.Path)
parser.add_argument("--revision", default="c183be7375c9c301de785dc4584b5cf0c0a2e951")
options = parser.parse_args()
repository = pathlib.Path(__file__).resolve().parents[2]
if options.destination.exists() and any(options.destination.iterdir()):
    raise SystemExit("The baseline destination must be empty.")
options.destination.mkdir(parents=True, exist_ok=True)
archive = subprocess.run(["git", "archive", options.revision], cwd=repository,
                         check=True, stdout=subprocess.PIPE).stdout
with tarfile.open(fileobj=io.BytesIO(archive)) as source:
    source.extractall(options.destination, filter="data")
harness = pathlib.Path(__file__).resolve().parent
shutil.copytree(harness, options.destination / "tools/ExcelRenderer.Performance",
                ignore=shutil.ignore_patterns("bin", "obj", "results"), dirs_exist_ok=True)
shutil.copyfile(repository / "src/ExcelRenderer/Rendering/ConversionMetrics.cs",
                options.destination / "src/ExcelRenderer/Rendering/ConversionMetrics.cs")


def replace(path, original, instrumented):
    """Require the expected old code so a changed baseline cannot silently diverge."""
    target = options.destination / path
    content = target.read_text()
    if original not in content:
        raise SystemExit(f"Baseline source has changed: {path}: {original}")
    target.write_text(content.replace(original, instrumented))


replace("src/ExcelRenderer/Properties/AssemblyInfo.cs",
        '[assembly: InternalsVisibleTo("ExcelRenderer.Tests")]',
        '[assembly: InternalsVisibleTo("ExcelRenderer.Tests")]\n'
        '[assembly: InternalsVisibleTo("ExcelRenderer.Performance")]')
converter = "src/ExcelRenderer/ExcelConverter.RenderAsync.cs"
replace(converter, "var fontManager = new FontManager(request.FontOptions);",
        'var fontManager = ConversionMetrics.Measure("fonts", () => new FontManager(request.FontOptions));')
replace(converter, "var document = new ExcelReader(fontManager).Read(source, diagnostics);",
        'var document = ConversionMetrics.Measure("reader", () => new ExcelReader(fontManager).Read(source, diagnostics));\n'
        '            ConversionMetrics.Report("cells", document.Sheets.Sum(sheet => sheet.Cells.Count));\n'
        '            ConversionMetrics.Report("textCells", document.Sheets.Sum(sheet => sheet.Cells.Values.Count(cell => !string.IsNullOrEmpty(cell.Text))));')
replace(converter, "CollectMissingGlyphDiagnostics(sheets, fontManager, diagnostics);",
        'ConversionMetrics.Measure("diagnostics", () =>\n'
        '            {\n'
        '                CollectMissingGlyphDiagnostics(sheets, fontManager, diagnostics);\n'
        '                return true;\n'
        '            });')
replace(converter, "await PdfSharpFontLock.WaitAsync(cancellationToken).ConfigureAwait(false);",
        'var lockTimer = System.Diagnostics.Stopwatch.StartNew();\n'
        '            await PdfSharpFontLock.WaitAsync(cancellationToken).ConfigureAwait(false);\n'
        '            ConversionMetrics.Report("fontLockWait.ms", lockTimer.Elapsed.TotalMilliseconds);')
replace(converter,
        "renderer.Render(page.Commands, page.Sheet.PageSettings with { Width = page.Descriptor.WidthPoints, Height = page.Descriptor.HeightPoints }, rendered);",
        'ConversionMetrics.Measure("pdfPage", () =>\n'
        '                    {\n'
        '                        renderer.Render(page.Commands, page.Sheet.PageSettings with { Width = page.Descriptor.WidthPoints, Height = page.Descriptor.HeightPoints }, rendered);\n'
        '                        return true;\n'
        '                    });\n'
        '                    ConversionMetrics.Report("pdfSave", 1);')
replace(converter, "result.AddPage(source.Pages[0]);",
        'result.AddPage(source.Pages[0]);\n'
        '                    ConversionMetrics.Report("pdfImport", 1);')
replace(converter, "result.Save(stream, false);",
        'ConversionMetrics.Measure("pdfFinalSave", () =>\n'
        '                {\n'
        '                    result.Save(stream, false);\n'
        '                    return true;\n'
        '                });\n'
        '                ConversionMetrics.Report("pdfSave", 1);')
replace("src/ExcelRenderer/Layout/ReportLayoutEngine.cs", "pass.Execute(context);",
        'ExcelRenderer.Rendering.ConversionMetrics.Measure(pass.GetType().Name, () =>\n'
        '            {\n'
        '                pass.Execute(context);\n'
        '                return true;\n'
        '            });')
replace("src/ExcelRenderer/Excel/ExcelReader.cs",
        "using var workbook = new XLWorkbook(workbookStream);",
        'using var workbook = ExcelRenderer.Rendering.ConversionMetrics.Measure("closedXml", () => new XLWorkbook(workbookStream));')
replace("src/ExcelRenderer/Fonts/FontManager.cs",
        "private static bool Supports(ResolvedFont font, string text)\n    {",
        'private static bool Supports(ResolvedFont font, string text)\n'
        '    {\n'
        '        ExcelRenderer.Rendering.ConversionMetrics.Report("supportChecks", 1);\n'
        '        ExcelRenderer.Rendering.ConversionMetrics.Report("typefaceCreated", 1);')
replace("src/ExcelRenderer/PdfSharp/PdfSharpTextMeasurer.cs",
        "private static (double Ascent, double Descent, double Leading) MeasureLineMetrics(ResolvedFont font, double size)\n    {",
        'private static (double Ascent, double Descent, double Leading) MeasureLineMetrics(ResolvedFont font, double size)\n'
        '    {\n'
        '        ExcelRenderer.Rendering.ConversionMetrics.Report("lineMetricsComputed", 1);\n'
        '        ExcelRenderer.Rendering.ConversionMetrics.Report("typefaceCreated", 1);')
print(f"Instrumented baseline {options.revision} exported to {options.destination}")

replace("src/ExcelRenderer/Excel/ExcelReader.cs",
        "using var typeface = SKTypeface.FromStream(stream) ??",
        'ExcelRenderer.Rendering.ConversionMetrics.Report("typefaceCreated", 1);\n'
        '                    using var typeface = SKTypeface.FromStream(stream) ??')
replace("src/ExcelRenderer/PdfSharp/PdfSharpTextMeasurer.cs",
        'internal static XFont CreateResolvedFont(ResolvedFont font, double size) =>\n'
        '        new(PdfSharpFontResolver.RegisterResolvedFont(font), size, XFontStyleEx.Regular);',
        'internal static XFont CreateResolvedFont(ResolvedFont font, double size)\n'
        '    {\n'
        '        ExcelRenderer.Rendering.ConversionMetrics.Report("xFontCreated", 1);\n'
        '        return new(PdfSharpFontResolver.RegisterResolvedFont(font), size, XFontStyleEx.Regular);\n'
        '    }')
replace("src/ExcelRenderer/PdfSharp/PdfSharpTextMeasurer.cs",
        "return new XFont(font.Family, font.Size, style);",
        'ExcelRenderer.Rendering.ConversionMetrics.Report("xFontCreated", 1);\n'
        '        return new XFont(font.Family, font.Size, style);')

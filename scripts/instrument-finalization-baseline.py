#!/usr/bin/env python3
"""Add observation-only counters to an isolated 38390a6 checkout; never use it for timed results."""
import argparse
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def instrument(root):
    def replace(name, old, new):
        path = root / name
        text = path.read_text()
        assert old in text, (name, old)
        path.write_text(text.replace(old, new))

    base = 'src/ExcelRenderer/'
    replace(base + 'SkiaSharp/SkiaTextDrawing.cs', 'internal void PaintLegacy(SKCanvas canvas, DrawTextCommand command)\n    {', 'internal void PaintLegacy(SKCanvas canvas, DrawTextCommand command)\n    {\n        Rendering.ConversionMetrics.Report("legacyTextPaints", 1);')
    replace(base + 'SkiaSharp/SkiaPdfDocumentWriter.cs', '        using var surface = SKSurface.CreateNull(1, 1);', '        ConversionMetrics.Report("pdfNullSurfaceCollections", 1);\n        using var surface = SKSurface.CreateNull(1, 1);')
    replace(base + 'PostScript/PostScriptDrawingContext.cs', '        using var bitmap = SKBitmap.FromImage(lease.Value.Raster);', '        Rendering.ConversionMetrics.Report("imageAlphaScans", 1);\n        using var bitmap = SKBitmap.FromImage(lease.Value.Raster);')
    replace(base + 'CoreIntegration/ConditionalFormattingSession.cs', '        var duplicates = rules.GroupBy', '        ConversionMetrics.Report("conditionalFormatting.rulePrioritySorts", 1);\n        var duplicates = rules.GroupBy')
    replace(base + 'CoreIntegration/ConditionalFormattingSession.cs', '                        if (++visits > candidateLimit)', '                        ConversionMetrics.Report("conditionalFormatting.displayCandidateVisits", 1);\n                        if (++visits > candidateLimit)')
    replace(base + 'CoreIntegration/ConditionalFormattingSession.cs', '            foreach (var rule in index.Query(address.Row, address.Row + 1).Where', '            ConversionMetrics.Report("conditionalFormatting.ruleListSearches", 1);\n            ConversionMetrics.Report("conditionalFormatting.ruleListSorts", 1);\n            foreach (var rule in index.Query(address.Row, address.Row + 1).Where')
    replace(base + 'Drawing/CommandTextFinalizer.cs', '        var measured = layout.Layout(command.Text, command.Style.Font, command.Bounds.Width, command.Style.WrapText);', '        Rendering.ConversionMetrics.Report("unfinalizedTextLayouts", 1);\n        var measured = layout.Layout(command.Text, command.Style.Font, command.Bounds.Width, command.Style.WrapText);')
    replace(base + 'Drawing/CommandTextFinalizer.cs', '            yield return command switch', '            var started = System.Diagnostics.Stopwatch.GetTimestamp();\n            var result = command switch')
    replace(base + 'Drawing/CommandTextFinalizer.cs', '                _ => command,\n            };', '                _ => command,\n            };\n\n            Rendering.ConversionMetrics.Report("textFinalize.ms", (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d / System.Diagnostics.Stopwatch.Frequency);\n            yield return result;')
    old = """        var lines = layout.Lines.Select(line => line.Text.Length == 0 || line.Runs.Count > 0
            ? line
            : line with { Runs = service.Layout(line.Text, command.Style.Font with { Size = size }, double.MaxValue, false).Lines.Single().Runs }).ToArray();"""
    new = """        var lines = layout.Lines.Select(line =>
        {
            if (line.Text.Length == 0 || line.Runs.Count > 0)
            {
                return line;
            }

            Rendering.ConversionMetrics.Report("fontRunCompletions", 1);
            return line with { Runs = service.Layout(line.Text, command.Style.Font with { Size = size }, double.MaxValue, false).Lines.Single().Runs };
        }).ToArray();
"""
    replace(base + 'Drawing/CommandTextFinalizer.cs', old, new)
    replace(base + 'Fonts/ResolvedGlyphLayout.cs', '        var resources = ConversionFontResources.Current', '        Rendering.ConversionMetrics.Report("glyphRunsFinalized", 1);\n        var resources = ConversionFontResources.Current')
    replace('src/ExcelRenderer.Core/Pdf/PdfFontCollection.cs', '            entry.Glyphs.UnionWith(glyphs);', '            ConversionMetrics.Report("pdfFontRunsCollected", 1);\n            entry.Glyphs.UnionWith(glyphs);')
    replace(base + 'ExcelConverter.Viewports.cs', 'DrawCommandBounds.Get(commands, original, fonts)', 'ConversionMetrics.Measure("bounds", () => DrawCommandBounds.Get(commands, original, fonts))')
    # Copy only the driver into this diagnostic checkout. Its source uses baseline APIs.
    for name in ('Program.cs', 'RenderMemoryBenchmark.cs', 'RenderMemoryBenchmark.Profile.cs'):
        (root / 'tools/ExcelRenderer.Performance' / name).write_bytes((ROOT / 'tools/ExcelRenderer.Performance' / name).read_bytes())


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('checkout', type=Path)
    instrument(parser.parse_args().checkout.resolve())

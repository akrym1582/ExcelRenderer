#!/usr/bin/env python3
"""Run targeted regression mutations, restoring every source byte even on failure.
Source scripts/test-fonts.sh first. Use --dotnet to select a dotnet executable/wrapper.
No mutation framework and no production mutation switches are installed.
"""
import argparse
import hashlib
import json
import pathlib
import re
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parent.parent
logs = root / 'TestResults/Mutations'
logs.mkdir(parents=True, exist_ok=True)

skia = 'src/ExcelRenderer/SkiaSharp/SkiaTextDrawing.cs'
pdf = 'src/ExcelRenderer/PdfSharp/PdfSharpFinalizedTextPainter.cs'
reader = 'src/ExcelRenderer/Excel/DrawingMLReader.cs'
mutations = []

def change(name, path, old, new, test, occurrence=0):
    mutations.append((name, path, old, new, test, occurrence))

change('skia-drawing-face', skia,
       'using var font = new SKFont(primaryTypeface, size);',
       'using var mutationFace = SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory, "Fonts/NotoSansMono-Regular.ttf"));\n'
       '            using var font = new SKFont(mutationFace, size);',
       'Skia_legacy_uses_selected_face_for_measurement_and_drawing')
change('pdf-run-x', pdf, 'positioned.Left + run.X,', 'positioned.Left,',
       'Pdf_finalized_text_uses_stored_run_origins_and_font_size')
change('pdf-baseline', pdf, '                            positioned.Baseline,',
       '                            command.Bounds.Y + 9,',
       'Pdf_finalized_text_uses_stored_run_origins_and_font_size')
underline = '''                    graphics.DrawLine(
                        new XPen(brush.Color),
                        positioned.Left,
                        positioned.Baseline + 1,
                        positioned.Left + positioned.Line.Width,
                        positioned.Baseline + 1);'''
change('pdf-double-underline', pdf, underline, underline + '\n' + underline,
       'Pdf_finalized_underline_is_drawn_once_per_line_at_stored_baseline')
change('svg-run-x', skia, '(float)(positioned.Left + run.X),',
       '(float)(positioned.Left + (_textAsPaths ? 0 : run.X)),',
       'Svg_serialized_finalized_text_matches_png_geometry')
change('reader-emu', 'src/ExcelRenderer.Core/Excel/DrawingMLReader.cs', 'emu / EmusPerPoint;', 'emu / (EmusPerPoint * 2);',
       'Reader_preserves_drawingml_anchor_coordinates')
change('reader-marker', 'src/ExcelRenderer.Core/Excel/DrawingMLReader.cs', 'uint.Parse(column) + 1', 'uint.Parse(column)',
       'Reader_preserves_drawingml_anchor_coordinates')
change('skia-legacy-restore', skia, '            canvas.Restore();', '            _ = canvas.SaveCount;',
       'Skia_legacy_restores_canvas_after_drawing_failure', 0)
change('skia-finalized-restore', skia, '            canvas.Restore();', '            _ = canvas.SaveCount;',
       'Skia_finalized_restores_canvas_after_drawing_failure', 1)
change('skia-rotation-restore', 'src/ExcelRenderer/SkiaSharp/SkiaTextPainter.cs',
       '            canvas.Restore();', '            _ = canvas.SaveCount;', 'Skia_legacy_restores_canvas_after_drawing_failure|Skia_finalized_restores_canvas_after_drawing_failure')
change('pdf-finalized-restore', pdf, '            graphics.Restore(state);', '            _ = graphics.Transform;',
       'Pdf_finalized_restores_graphics_after_drawing_failure')
change('pdf-legacy-resolved-restore', 'src/ExcelRenderer/PdfSharp/PdfSharpLegacyTextPainter.cs',
       '            graphics.Restore(state);', '            _ = graphics.Transform;', 'Pdf_legacy_restores_graphics_after_drawing_failure', 1)
change('pdf-legacy-ordinary-restore', 'src/ExcelRenderer/PdfSharp/PdfSharpLegacyTextPainter.cs',
       '            graphics.Restore(state);', '            _ = graphics.Transform;', 'Pdf_legacy_ordinary_text_preserves_caller_transform_and_clip', 0)
change('pdf-rotation-restore', 'src/ExcelRenderer/PdfSharp/PdfSharpTextPainter.cs',
       '            graphics.Restore(state);', '            _ = graphics.Transform;', 'Pdf_legacy_restores_graphics_after_drawing_failure|Pdf_finalized_restores_graphics_after_drawing_failure')

change('trim-origin', 'src/ExcelRenderer/Rendering/PageViewport.cs',
       '-Crop.X + Padding, -Crop.Y + Padding', 'Padding, Padding',
       'Artificial_scene_trim_and_PDF_annotations_have_independent_numeric_expectations')
change('pdf-link-y', 'src/ExcelRenderer/PdfSharp/PdfHyperlinkWriter.cs',
       'height - b.Y - b.Height', 'b.Y',
       'Artificial_scene_trim_and_PDF_annotations_have_independent_numeric_expectations')
change('pdf-destination-selection', 'src/ExcelRenderer/ExcelConverter.Viewports.cs',
       'targetPage?.Descriptor.OutputPageNumber', 'targetPage?.Descriptor.DocumentPageNumber',
       'Generated_PDF_destination_references_final_selected_document_page')
change('merged-source-outside-range', 'src/ExcelRenderer/CoreIntegration/FullCellSelection.cs',
       'requestedRange && isMerged', '!requestedRange && isMerged',
       'Generated_range_replaces_print_area_and_preserves_partial_merge_geometry')
change('markdown-href-escaping', 'src/ExcelRenderer/Markdown/MarkdownHyperlinks.cs',
       'Html(uri)', 'uri', 'Markdown_links_anchors_lists_and_HTML_attributes_are_safe_and_None_is_plain')

originals = {path: (root / path).read_bytes() for _, path, *_ in mutations}
results = []
try:
    for name, path, old, new, test, occurrence in mutations:
        original = originals[path]
        text = original.decode()
        positions = [m.start() for m in re.finditer(re.escape(old), text)]
        if len(positions) <= occurrence:
            raise RuntimeError(f'{name}: source site not found')
        index = positions[occurrence]
        altered = text[:index] + new + text[index + len(old):]
        try:
            (root / path).write_text(altered)
            proc = subprocess.run([args.dotnet, 'test', 'tests/ExcelRenderer.Tests', '--configuration', 'Release',
                                   '--no-restore', '-m:1', '--filter', test], cwd=root, capture_output=True, text=True)
            output = proc.stdout + proc.stderr
            (logs / (name + '.log')).write_text(output)
            # A compiler error, crash or "no tests matched" is not a successful red detection.
            detected = proc.returncode != 0 and re.search(r'Failed!\s+- Failed:\s+[1-9]\d*', output) is not None
            summary = next((line for line in output.splitlines() if line.startswith('Failed!')), 'NO TEST FAILURE SUMMARY')
            results.append({'mutation': name, 'test_filter': test, 'detected': detected, 'summary': summary})
            print(name + ': ' + summary, flush=True)
            if not detected:
                raise RuntimeError(f'{name}: regression test did not detect the mutation; see log')
        finally:
            (root / path).write_bytes(original)
finally:
    for path, original in originals.items():
        (root / path).write_bytes(original)
    restored = all((root / path).read_bytes() == original for path, original in originals.items())
    (logs / 'results.json').write_text(json.dumps({'results': results, 'sources_restored': restored,
        'source_sha256': {p: hashlib.sha256(b).hexdigest() for p, b in originals.items()}}, indent=2) + '\n')
    assert restored, 'Original source bytes were not restored'

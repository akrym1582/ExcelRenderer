#!/usr/bin/env python3
"""Prove reviewed AA and managed subset metadata cannot hide content/font corruption."""
import argparse
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

import fitz
from fontTools.ttLib import TTFont
from pdf_native_evidence import REFERENCE, evidence

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, default=ROOT / "artifacts/pdf-comparison")
    args = parser.parse_args()
    for mutation in ("remove-ivs", "extra-ink", "font-metrics", "font-bounds"):
        with tempfile.TemporaryDirectory(prefix="pdf-comparison-mutation-") as work:
            work = Path(work)
            target = work / "commands-1"
            shutil.copytree(args.baseline / "commands-1", target)
            source = target / "skiasharp.pdf"
            document = fitz.open(source)
            if mutation == "remove-ivs":
                document[0].add_redact_annot(fitz.Rect(19, 52, 32, 67), fill=(1, 1, 1))
                document[0].apply_redactions()
            elif mutation == "extra-ink":
                document[0].draw_line((20, 52), (50, 67), color=(0, 0, 0), width=.2)
            else:
                # Change raw and finalized PDFs together so byte preservation
                # cannot mask a defective subsetter's font/descriptor output.
                for document in (document, fitz.open(target / 'skiasharp-raw.pdf')):
                    font_info = next(font for font in document[0].get_fonts(full=True)
                                     if document.extract_font(font[0])[3])
                    descendant = int(REFERENCE.findall(document.xref_get_key(font_info[0], 'DescendantFonts')[1])[0])
                    descriptor = int(REFERENCE.findall(document.xref_get_key(descendant, 'FontDescriptor')[1])[0])
                    if mutation == 'font-bounds':
                        document.xref_set_key(descriptor, 'FontBBox', '[0 0 1 1]')
                    else:
                        font = TTFont(io.BytesIO(document.extract_font(font_info[0])[3]))
                        gid = next(char[1] for trace in document[0].get_texttrace()
                                   if trace['font'] == font['name'].getDebugName(6) for char in trace['chars'] if char[1] > 0)
                        name = font.getGlyphOrder()[gid]
                        width, bearing = font['hmtx'][name]
                        font['hmtx'][name] = (width + 10, bearing)
                        output = io.BytesIO()
                        font.save(output)
                        font_stream = int(REFERENCE.findall(document.xref_get_key(descriptor, 'FontFile2')[1])[0])
                        document.update_stream(font_stream, output.getvalue())
                    path = Path(document.name)
                    document.save(path.with_suffix('.mutated.pdf'))
                    document.close()
                    path.with_suffix('.mutated.pdf').replace(path)
                proof = evidence(target)
                expected = 'Changed source metrics' if mutation == 'font-metrics' else 'descriptor FontBBox'
                if proof['passed'] or not any(expected in error for error in proof['errors']):
                    raise SystemExit(f'Mutation escaped independent font evidence: {mutation}: {proof}')
                print(f'Detected {mutation}: independent font evidence rejected corruption', flush=True)
                continue
            document.save(target / "mutated.pdf")
            document.close()
            (target / "mutated.pdf").replace(source)
            process = subprocess.run([sys.executable, str(ROOT / "scripts/compare-pdf-backends.py"), "--no-generate", "--fixture", "commands-1", "--output", str(work)], cwd=ROOT)
            report = json.loads((work / "report.json").read_text())
            if process.returncode == 0 or report["requiresFix"] == 0:
                raise SystemExit(f"Mutation escaped comparison: {mutation}")
            print(f"Detected {mutation}: {report['requiresFix']} unmet gates", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())

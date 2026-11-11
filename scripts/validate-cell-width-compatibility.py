#!/usr/bin/env python3
"""Verify the reviewed wrapped-cell dimensions without rewriting the input or reference PDF."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

import fitz

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / 'src/ExcelRenderer.Tool/bin/Release/net10.0/ExcelRenderer.Tool.dll'
SOURCE = ROOT / 'docs/validation/pr113-display-followup/additional-forms/wrapped/after.xlsx'
REFERENCE = ROOT / 'docs/validation/pr113-display-followup/additional-forms/wrapped/after/libreoffice/after.pdf'
FONT = ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def dimensions(pdf):
    with fitz.open(pdf) as document:
        assert len(document) == 1
        fill = next(p for p in document[0].get_drawings() if p['fill'] and abs(p['fill'][0] - .9412) < .001)
        rect = fill['rect']
        return {'rect_pt': list(rect), 'width_pt': rect.width, 'height_pt': rect.height,
                'text': document[0].get_text()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--cli', type=Path, default=CLI, help='Built CLI DLL or installed executable.')
    args = parser.parse_args()
    output = args.run.resolve()
    output.mkdir(parents=True, exist_ok=False)
    report = {'source_sha256': sha(SOURCE), 'reference_sha256': sha(REFERENCE),
              'reference': dimensions(REFERENCE), 'records': []}
    for engine in ('main', 'core'):
        for mode in ('source', 'libreoffice', 'libreoffice-auto'):
            pdf = output / (engine + '-' + mode + '.pdf')
            executable = ['dotnet', args.cli.resolve()] if args.cli.suffix == '.dll' else [args.cli.resolve()]
            command = executable + ['render' if engine == 'main' else 'core', SOURCE,
                       '-o', pdf, '--font-file', FONT, '--column-width-metrics', 'source' if mode == 'source' else 'libreoffice']
            if mode == 'libreoffice':
                # The retained reference resolves Calibri 11 to Noto Sans Mono:
                # 0.6 em * 11pt * 96/72 = 8.8px. Drawing remains Noto Sans JP.
                command += ['--column-width-mdw', '8.8']
            if engine == 'main':
                command += ['--format', 'pdf', '--no-system-fonts', '--fallback-font', 'Noto Sans JP']
            result = subprocess.run(list(map(str, command)), cwd=ROOT, capture_output=True, text=True)
            assert result.returncode == 0, result.stderr
            size = dimensions(pdf)
            assert abs(size['height_pt'] - 120) < .001
            expected = {'source': 219.75, 'libreoffice': 276.5472666, 'libreoffice-auto': 255.5967161}[mode]
            assert abs(size['width_pt'] - expected) < .001
            if mode == 'libreoffice':
                assert abs(size['width_pt'] - report['reference']['width_pt']) < .12
            report['records'].append({'engine': engine, 'mode': mode, 'command': list(map(str, command)),
                                      'stderr': result.stderr, 'pdf_sha256': sha(pdf), **size})
    for mode in ('source', 'libreoffice', 'libreoffice-auto'):
        left, right = [r for r in report['records'] if r['mode'] == mode]
        assert left['rect_pt'] == right['rect_pt']
    assert report['source_sha256'] == sha(SOURCE)
    (output / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print('Both products: default width 219.75pt; compatible width 276.547pt; saved height 120pt; reference width error <0.12pt.')


if __name__ == '__main__':
    main()

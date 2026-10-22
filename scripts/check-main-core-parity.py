#!/usr/bin/env python3
"""Compare the public-API parity outputs with the existing Linux semantic/raster gate."""
import argparse
import json
from pathlib import Path

import fitz

from libreoffice_regression import ROOT, compare

CASES = ('values text dimensions-match dimensions-mismatch borders portrait landscape '
         'scale fit breaks titles areas empty selection images header-footer defaults border-conflict image-overlap').split()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, default=ROOT / 'tests/ExcelRenderer.Tests/bin/Release/net10.0/SampleOutputs/MainCoreParity')
    args = parser.parse_args()
    records = []
    for case in CASES:
        execution = json.loads((args.run / (case + '.json')).read_text())
        if execution['status'] != 'passed':
            raise ValueError('Semantic test did not pass: ' + case)
        record = compare(args.run / (case + '-main.pdf'), args.run / (case + '-core.pdf'), differences=args.run / 'differences' / case)
        # Hand-specified physical expectations also detect a shared implementation error.
        independent = []
        for engine in ('main', 'core'):
            with fitz.open(args.run / (case + '-' + engine + '.pdf')) as pdf:
                page = pdf[0]
                if case == 'scale':
                    sizes = [span['size'] for block in page.get_text('dict')['blocks'] if 'lines' in block
                             for line in block['lines'] for span in line['spans']]
                    if not sizes or any(abs(size - 11 * 0.75) > 0.01 for size in sizes):
                        independent.append(engine + ': 11pt at 75% must be 8.25pt')
                if case in ('images', 'image-overlap'):
                    # B2: 36pt margin + 20 column units * 8px MDW * 72/96;
                    # row 1 is explicitly 30pt. 28575 EMU / 12700 = 2.25pt.
                    expected_count = 1 if case == 'images' else 2
                    images = page.get_image_info()
                    if len(images) != expected_count or any(
                            any(abs(a - b) > 0.01 for a, b in zip(item['bbox'], (156, 66, 158.25, 68.25)))
                            for item in images):
                        independent.append(engine + ': incorrect image count/bounds')
                    color = (255, 0, 0) if case == 'images' else (0, 0, 255)
                    if page.get_pixmap(matrix=fitz.Matrix(4, 4), alpha=False).pixel(626, 266) != color:
                        independent.append(engine + ': source pixels/overlap order changed')
                if case == 'borders':
                    if page.get_pixmap(matrix=fitz.Matrix(4, 4), alpha=False).pixel(200, 180) != (255, 255, 0):
                        independent.append(engine + ': expected yellow background')
        if independent:
            record['passed'] = False
            record['errors'].extend(independent)
        record['independent_errors'] = independent
        record.update(execution)
        records.append(record)
    (args.run / 'raster-report.json').write_text(json.dumps(records, ensure_ascii=False, indent=2) + '\n')
    failed = [record['id'] for record in records if not record['passed']]
    print(f'{len(records)} direct comparisons at 144/288dpi; failures: {failed}')
    if failed:
        raise SystemExit(1)


if __name__ == '__main__':
    main()

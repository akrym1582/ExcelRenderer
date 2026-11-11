#!/usr/bin/env python3
"""Independently check the reviewed 05-borders page assignment and physical strokes."""
import argparse
import json
from pathlib import Path

import fitz
from libreoffice_regression import ROOT, sha

# Hand-derived from XLSX: 48pt margins; A:C = 120pt, D = 140.25pt;
# row 1 = 38pt, row 2 = 18pt, row 3 = 48pt, row 4 = 28pt, row 5 = 48pt.
# At 100%, A:C fit on page 1; D starts at the left margin of page 2.
GRAY = (.1569, .1569, .1569)
BLUE = (.1961, .3922, .7843)
RED = (.8627, .2353, .2353)
GREEN = (.1569, .5882, .3529)
PURPLE = (.549, .2745, .7059)
LABELS = [['細線（黒）', '中線（青）', '太線（赤）', '上のみ', '右のみ', '下のみ'],
          ['辺ごとの色と太さ', '左のみ']]


def stroke(a, b, width, color):
    return [*a, *b, width, *color]


def expected_strokes():
    first = []
    # Shared vertical boundaries use the stronger neighboring stroke.
    for x, width, color in [(48, .5, GRAY), (168, 1, BLUE), (288, 2, RED), (408, 2, RED)]:
        first.append(stroke((x, 104), (x, 152), width, color))
    for start, width, color in [(48, .5, GRAY), (168, 1, BLUE), (288, 2, RED)]:
        for y in [104, 152]:
            first.append(stroke((start, y), (start + 120, y), width, color))
    first.extend([stroke((48, 180), (168, 180), 1, BLUE),
                  stroke((288, 180), (288, 228), 2, RED),
                  stroke((288, 228), (408, 228), .5, GREEN),
                  stroke((408, 180), (408, 228), 1, PURPLE)])
    second = [stroke((48, 104), (48, 152), 2, RED),
              stroke((188.25, 104), (188.25, 152), .5, GREEN),
              stroke((48, 104), (188.25, 104), 1, BLUE),
              stroke((48, 152), (188.25, 152), 2, PURPLE),
              stroke((48, 180), (48, 228), 1, PURPLE)]
    return [first, second]


def audit(path, engine):
    pages = []
    with fitz.open(path) as document:
        assert len(document) == 2, (engine, 'page count')
        for index, (page, wanted) in enumerate(zip(document, expected_strokes())):
            text = page.get_text()
            for other, labels in enumerate(LABELS):
                for label in labels:
                    assert text.count(label) == (1 if index == other else 0), (engine, index, label)
            assert text.count('Excel 罫線のPDF描画') == (1 if index == 0 else 0)
            assert f'ページ {index + 1} / 2' in text
            actual, fills = [], []
            for drawing in page.get_drawings():
                if drawing['type'] == 'f':
                    fills.extend([list(item[1]) for item in drawing['items'] if item[0] == 're'])
                else:
                    for item in drawing['items']:
                        assert item[0] == 'l', (engine, 'unexpected drawing', item)
                        actual.append(stroke(item[1], item[2], drawing['width'], drawing['color']))
            assert len(actual) == len(wanted), (engine, index, 'stroke count')
            for line in wanted:
                found = next((i for i, candidate in enumerate(actual) if all(abs(a-b) < .002 for a, b in zip(line, candidate))), None)
                assert found is not None, (engine, index, 'missing or changed stroke', line)
                actual.pop(found)
            # Preserve title fill on page 1 and every individual cell fill on both pages.
            expected_fills = ([[48, 48, 548.25, 86]] + [[x, y, x + 120, y + 48] for y in [104, 180] for x in [48, 168, 288]]
                              if index == 0 else [[48, y, 188.25, y + 48] for y in [104, 180]])
            assert len(fills) == len(expected_fills)
            assert all(all(abs(a-b) < .002 for a, b in zip(left, right)) for left, right in zip(fills, expected_fills))
            pages.append({'page': index + 1, 'text': text, 'strokes': wanted, 'fills': fills,
                          'glyphs': [{'text': span['text'], 'bbox': span['bbox']} for block in page.get_text('dict')['blocks'] if 'lines' in block for line in block['lines'] for span in line['spans']]})
    return {'engine': engine, 'pdf_sha256': sha(path), 'pages': pages, 'passed': True}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, default=ROOT / 'artifacts/libreoffice-regression')
    args = parser.parse_args()
    records = [audit(args.run / 'actual' / engine / '05-borders.pdf', engine) for engine in ['main', 'core']]
    # The retained Calc comparison is evidence of page assignment, not a pixel oracle.
    reference = ROOT / 'docs/validation/libreoffice-reference/pdf/05-borders.pdf'
    with fitz.open(reference) as document:
        assert len(document) == 2
        for index, page in enumerate(document):
            for other, labels in enumerate(LABELS):
                for label in labels:
                    assert page.get_text().count(label) == (1 if index == other else 0)
    report = {'basis': 'XLSX rows/columns/styles and retained Calc page assignment', 'tolerance_pt': .002,
              'libreoffice_sha256': sha(reference), 'records': records,
              'known_differences': ['Calc glyph/column metrics differ', 'Calc header Y differs and omits page footer',
                                    'Calc repeats the merged title background on page 2; product does not']}
    (args.run / 'borders-pagination-audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print('05-borders: both engines, both pages, all labels, 19 strokes and 9 fills verified')


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Audit the five fixed border sheets against independent numeric expectations.

Run after compare-libreoffice.py has generated main/Core PDFs. Expectations below
are hand-calculated from the fixed XML and the documented provisional product
border policy, not copied from either renderer's output or an Excel oracle.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path

import fitz

ROOT = Path(__file__).resolve().parents[1]
BLACK = [0, 0, 0]
WHITE = [1, 1, 1]


def stroke(x1, y1, x2, y2, width, color=BLACK):
    return {'start': [x1, y1], 'end': [x2, y2], 'width_pt': width, 'color': color}


# Fixed XML: Noto Sans JP Normal 11pt (MDW 8), col width 8 => 48pt,
# rows 30pt, margins 0.25in => 18pt. No main/Core model or layout API is used.
# Double uses the independent existing contract: width .75 at boundary +/-.75.
DOUBLE = [stroke(65.25, 18, 65.25, 48, .75), stroke(66.75, 18, 66.75, 48, .75),
          stroke(18, 107.25, 66, 107.25, .75), stroke(18, 108.75, 66, 108.75, .75),
          stroke(65.25, 168, 65.25, 198, .75), stroke(66.75, 168, 66.75, 198, .75)]
COLORS = [stroke(66, 18, 66, 48, .5, WHITE),
          stroke(65.25, 78, 65.25, 108, .75), stroke(66.75, 78, 66.75, 108, .75),
          stroke(114, 78, 114, 108, 2)]
MERGED = [stroke(18, 18, 18, 48, .5), stroke(18, 18, 66, 18, .5),
          stroke(113.25, 18, 113.25, 48, .75), stroke(114.75, 18, 114.75, 48, .75),
          stroke(114, 48, 114, 78, .5), stroke(66, 78, 114, 78, .5)]
DEFAULTS = [stroke(18, 47.25, 66, 47.25, .75), stroke(18, 48.75, 66, 48.75, .75),
            stroke(18, 77.25, 66, 77.25, .75), stroke(18, 78.75, 66, 78.75, .75),
            stroke(65.25, 78, 65.25, 108, .75), stroke(66.75, 78, 66.75, 108, .75)]


def values(line):
    ends = sorted([line['start'], line['end']])
    return [*ends[0], *ends[1], line['width_pt'], *line['color']]


def close(first, second):
    return all(abs(a - b) <= .002 for a, b in zip(values(first), values(second)))


def difference(expected, actual):
    remaining = list(actual)
    missing = []
    for line in expected:
        index = next((i for i, candidate in enumerate(remaining) if close(line, candidate)), None)
        if index is None:
            missing.append(line)
        else:
            remaining.pop(index)
    return {'matches': not missing and not remaining, 'missing': missing, 'unexpected': remaining}


def read(path):
    pages = []
    with fitz.open(path) as pdf:
        for number, page in enumerate(pdf, 1):
            strokes = []
            non_lines = []
            for drawing in page.get_drawings():
                for item in drawing['items']:
                    if drawing['type'] != 's' or item[0] != 'l':
                        non_lines.append(str(item))
                        continue
                    strokes.append(stroke(*item[1], *item[2], drawing['width'], list(drawing['color'])))
            duplicates = [line for i, line in enumerate(strokes) if any(close(line, earlier) for earlier in strokes[:i])]
            pages.append({'page': number, 'size_pt': list(page.rect), 'strokes': strokes,
                          'duplicate_strokes': duplicates, 'non_line_drawings': non_lines})
    return pages


def mutation_checks():
    baseline = copy.deepcopy(COLORS)
    altered_color = copy.deepcopy(baseline)
    altered_color[0]['color'] = BLACK
    shifted = copy.deepcopy(baseline)
    shifted[0]['start'][0] += 1
    too_wide = copy.deepcopy(baseline)
    too_wide[0]['width_pt'] *= 2
    mutations = {'missing_stroke': baseline[1:], 'duplicate_stroke': baseline + [baseline[0]],
                 'wrong_color': altered_color, 'shifted_endpoint': shifted, 'wrong_width': too_wide}
    assert difference(baseline, list(reversed(baseline)))['matches']
    for label, changed in mutations.items():
        assert not difference(baseline, changed)['matches'], label
    return {label: 'detected' for label in mutations}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, default=ROOT / 'artifacts/libreoffice-comparison/core-pagination-fix')
    parser.add_argument('--output', type=Path, default=ROOT / 'docs/validation/libreoffice-comparison/core-pagination-fix/border-audit.json')
    args = parser.parse_args()
    fixture = 'tests/ExcelRenderer.Tests/SampleInputs/BorderResolution/border-conflicts.xlsx'
    manifest = json.loads((ROOT / 'docs/validation/libreoffice-reference/manifest.json').read_text())
    case = next(c for c in manifest['cases'] if c['id'] == 'border-conflicts')
    assert hashlib.sha256((ROOT / fixture).read_bytes()).hexdigest() == case['input_sha256']
    report = {'input': fixture, 'input_sha256': case['input_sha256'],
              'expectation_basis': ['Fixed XML and manual point coordinates',
                                    'tests/ExcelRenderer.Tests/SharedBorderWorkbookTests.cs',
                                    'docs/validation/border-resolution/README.md'],
              'tolerance_pt': .002, 'excel_compatibility': 'not established',
              'mutation_checks': mutation_checks(), 'engines': {}}
    for engine in ['main', 'core', 'libreoffice']:
        path = (ROOT / 'docs/validation/libreoffice-reference/pdf/border-conflicts.pdf' if engine == 'libreoffice'
                else args.run / 'actual' / engine / 'border-conflicts.pdf')
        pages = read(path)
        assert len(pages) == 5
        expected = [DOUBLE, COLORS, MERGED, DEFAULTS, [stroke(66, 18, 66, 48, .5 if engine == 'main' else 2)]]
        for page, wanted in zip(pages, expected):
            if engine != 'libreoffice':
                page['expected'] = wanted
                page['independent_expectation'] = difference(wanted, page['strokes'])
                assert page['independent_expectation']['matches'], (engine, page)
                assert not page['duplicate_strokes'] and not page['non_line_drawings'], (engine, page)
        report['engines'][engine] = {'pdf_sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                                      'stroke_count': sum(len(p['strokes']) for p in pages),
                                      'duplicate_count': sum(len(p['duplicate_strokes']) for p in pages), 'pages': pages}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print('main/Core: 5 sheets, 23 physical strokes each match independent expectations; no duplicate strokes')
    print('Calc duplicate strokes:', report['engines']['libreoffice']['duplicate_count'])


if __name__ == '__main__':
    main()

"""Exact follow-ups for restoring saved leading blank dimensions; frozen PDFs stay intact."""
from functools import lru_cache
import hashlib
import json
from pathlib import Path

import fitz

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'tests/Shared/PrintOriginReview'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


@lru_cache(maxsize=1)
def contracts():
    data = json.loads((BASE / 'manifest.json').read_text())
    if set(data) != {'full/image-anchor', 'appearance/main/09-cell-border', 'appearance/core/09-cell-border'}:
        raise ValueError('Unexpected print-origin review scope')
    return data


def translation_errors(before, after, dx, dy, dotted_phase_paths=()):
    """Check retained text, strokes and image resources against the specified source-axis shift."""
    errors = []
    def near(a, b):
        return len(a) == len(b) and all(abs(x - y) <= .001 for x, y in zip(a, b))
    def rect(r):
        return [r[0] + dx, r[1] + dy, r[2] + dx, r[3] + dy]
    if before.mediabox != after.mediabox or before.cropbox != after.cropbox or before.rotation != after.rotation:
        errors.append('Page boxes/rotation changed')
    if before.get_links() or after.get_links():
        errors.append('Origin review does not cover link annotations')
    a, b = before.get_texttrace(), after.get_texttrace()
    if len(a) != len(b):
        errors.append('Text run count changed')
    for x, y in zip(a, b):
        if {k: v for k, v in x.items() if k not in ('bbox', 'chars', 'seqno')} != {k: v for k, v in y.items() if k not in ('bbox', 'chars', 'seqno')}:
            errors.append('Text styling changed')
        if len(x['chars']) != len(y['chars']):
            errors.append('Character count changed')
        for left, right in zip(x['chars'], y['chars']):
            if left[:2] != right[:2] or not near([left[2][0] + dx, left[2][1] + dy], right[2]) or not near(rect(left[3]), right[3]):
                errors.append('Text changed beyond the specified translation')
    a, b = before.get_image_info(hashes=True), after.get_image_info(hashes=True)
    if len(a) != len(b):
        errors.append('Image count changed')
    for x, y in zip(a, b):
        if {k: v for k, v in x.items() if k not in ('number', 'xref', 'bbox', 'transform')} != {k: v for k, v in y.items() if k not in ('number', 'xref', 'bbox', 'transform')}:
            errors.append('Image pixels or dimensions changed')
        matrix = list(x['transform']); matrix[4] += dx; matrix[5] += dy
        if not near(matrix, y['transform']) or not near(rect(x['bbox']), y['bbox']):
            errors.append('Image placement changed beyond the specified translation')
    def move(value):
        if isinstance(value, fitz.Point):
            return fitz.Point(value.x + dx, value.y + dy)
        if isinstance(value, fitz.Rect):
            return fitz.Rect(rect(value))
        if isinstance(value, (tuple, list)):
            return [move(v) for v in value]
        return value
    a, b = before.get_drawings(), after.get_drawings()
    if len(a) != len(b):
        errors.append('Paint count changed')
    def dots(path):
        items = path['items']
        if path['type'] != 'f' or path['fill'] != (0, 0, 0) or len(items) % 3:
            return False
        starts = []
        for i in range(0, len(items), 3):
            lines = items[i:i + 3]
            if any(line[0] != 'l' for line in lines):
                return False
            a, b, c, d = lines[0][1], lines[0][2], lines[1][2], lines[2][2]
            if not near(b, lines[1][1]) or not near(c, lines[2][1]) or abs(a.y - b.y) > .001 or abs(b.x - c.x) > .001 or abs(c.y - d.y) > .001 or abs(a.x - d.x) > .001:
                return False
            if not -.001 <= b.x - a.x <= .301 or abs(c.y - a.y - .3) > .001:
                return False
            starts.append(a.x)
        return bool(starts) and all(abs(y - x - .9) <= .001 for x, y in zip(starts, starts[1:]))
    for index, (x, y) in enumerate(zip(a, b)):
        phase = index in dotted_phase_paths
        if phase and (not dots(x) or not dots(y) or not near([x['rect'].y0 + dy, x['rect'].y1 + dy], [y['rect'].y0, y['rect'].y1]) or abs(x['rect'].x0 + dx - y['rect'].x0) > .901 or abs(x['rect'].x1 + dx - y['rect'].x1) > .901):
            errors.append('Dotted strip changed outside its fixed 0.3pt dots / 0.9pt period')
        for key in x:
            if phase and key in ('items', 'rect'):
                continue
            if key in ('seqno', 'layer'):
                continue
            left = move(x[key]) if key in ('items', 'rect') else x[key]
            right = y[key]
            def equal(v, w):
                if isinstance(v, (tuple, list, fitz.Point, fitz.Rect)):
                    return len(v) == len(w) and all(equal(i, j) for i, j in zip(v, w))
                if isinstance(v, (int, float)) and isinstance(w, (int, float)):
                    return abs(v - w) <= .001
                return v == w
            if not equal(left, right):
                errors.append('Paint changed beyond the specified translation: ' + key)
    return errors


def checked_pdf(key, historical):
    from shared_border_evidence import page_snapshot
    record = contracts().get(key)
    if record is None:
        return None
    expected = BASE / record['pdf']
    if sha(ROOT / record['input']) != record['input_sha256'] or sha(historical) != record['historical_pdf_sha256'] or sha(expected) != record['pdf_sha256']:
        raise ValueError('Print-origin input or PDF hash mismatch: ' + key)
    with fitz.open(historical) as before, fitz.open(expected) as after:
        if len(before) != len(after) or len(after) != len(record['pages']):
            raise ValueError('Print-origin page count changed: ' + key)
        for index, (left, right) in enumerate(zip(before, after), 1):
            errors = translation_errors(left, right, *record['shift_points'], record.get('dotted_phase_paths', ()))
            if errors or page_snapshot(left) != record['pages'][str(index)]['before'] or page_snapshot(right) != record['pages'][str(index)]['after']:
                raise ValueError('Print-origin evidence mismatch: ' + key + ': ' + str(errors))
    return expected


def evidence(before, after, name, number):
    from shared_border_evidence import page_snapshot
    key = 'full/' + name
    record = contracts().get(key)
    if record is None:
        return None
    checked_pdf(key, ROOT / record['historical_pdf'])
    row = record['pages'][str(number)]
    errors = translation_errors(before, after, *record['shift_points'])
    if page_snapshot(before) != row['before'] or page_snapshot(after) != row['after']:
        errors.append('Current PDF differs from the exact print-origin contract')
    return {'passed': not errors, 'errors': errors, 'shift_points': record['shift_points']}

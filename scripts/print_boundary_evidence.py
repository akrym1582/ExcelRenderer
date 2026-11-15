"""Bind automatic print-boundary cropping to exact references and unchanged source geometry."""
from functools import lru_cache
import hashlib
import json
from pathlib import Path

import fitz

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'tests/Shared/PrintBoundaryReview'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


@lru_cache(maxsize=1)
def contracts():
    data = json.loads((BASE / 'manifest.json').read_text())
    allowed = {'full/rich-theme', 'full/pages-selection', 'full/missing-glyph'}
    allowed |= {'appearance/' + engine + '/' + case for engine in ('main', 'core') for case in ('07-multiple-sheets', 'cf-phase1')}
    if set(data) != allowed:
        raise ValueError('Unexpected print-boundary follow-up scope')
    return data


def unclipped_commands(lines, clips):
    result = []
    for line in lines:
        if line.startswith('DrawViewportCommand'):
            body = json.loads(line[len('DrawViewportCommand'):])
            if body['Clip'] not in clips or body['OffsetX'] != 0 or body['OffsetY'] != 0:
                raise ValueError('Unexpected viewport change')
            result.extend(unclipped_commands(body['children'], clips))
        else:
            result.append(line)
    return result


def crop_errors(before, after, clip):
    from shared_border_evidence import normalized
    errors = []
    if before.mediabox != after.mediabox or before.cropbox != after.cropbox or before.rotation != after.rotation:
        errors.append('Page geometry changed')
    if normalized(before.get_drawings()) != normalized(after.get_drawings()):
        errors.append('Paint geometry changed')
    def images(page):
        return [{k: v for k, v in x.items() if k not in ('number', 'xref')} for x in page.get_image_info(hashes=True)]
    if images(before) != images(after) or before.get_links() != after.get_links():
        errors.append('Images or links changed')
    def chars(page):
        return [c for run in page.get_texttrace() for c in run['chars']]
    style_keys = ('font', 'size', 'color', 'opacity', 'dir', 'type', 'wmode')
    for run in after.get_texttrace():
        if not any(all(run.get(k) == old.get(k) for k in style_keys) for old in before.get_texttrace()):
            errors.append('Text styling changed')
    remaining = chars(before)
    for c in chars(after):
        found = next((i for i, x in enumerate(remaining) if x[:2] == c[:2] and max(abs(a-b) for a,b in zip(x[2],c[2])) <= .001), None)
        if found is None:
            errors.append('Source characters or glyph positions changed')
        else:
            remaining.pop(found)
    left, right = clip['X'], clip['X'] + clip['Width']
    if any(c[3][0] >= left + .001 and c[3][2] <= right - .001 and c[3][2] > c[3][0] for c in remaining):
        errors.append('A character wholly inside the print boundary was removed')
    return errors


def checked_pdf(key, historical):
    from shared_border_evidence import page_snapshot
    record = contracts().get(key)
    if record is None:
        return None
    expected = BASE / record['pdf']
    if sha(historical) != record['historical_pdf_sha256'] or sha(expected) != record['pdf_sha256']:
        raise ValueError('Print-boundary reference hash mismatch')
    if 'input' in record and sha(ROOT / record['input']) != record['input_sha256']:
        raise ValueError('Print-boundary input changed')
    with fitz.open(historical) as a, fitz.open(expected) as b:
        if len(a) != len(b) or len(b) != len(record['pages']):
            raise ValueError('Print-boundary page count changed')
        for i,(left,right) in enumerate(zip(a,b),1):
            row = record['pages'][str(i)]
            errors = crop_errors(left, right, row['clip'])
            if errors or page_snapshot(left) != row['before'] or page_snapshot(right) != row['after']:
                raise ValueError('Print-boundary proof failed: ' + key + ': ' + str(errors))
    if key.startswith('full/'):
        name = key.split('/')[1]
        for number,row in record['pages'].items():
            original = ROOT / 'tests/Shared/PdfRemovalBaseline' / name / ('skiasharp-page-' + number + '-commands.jsonl')
            if sha(original) != row['historical_commands_sha256'] or unclipped_commands(row['commands'], [row['clip']]) != original.read_text().splitlines():
                raise ValueError('Commands changed beyond zero-offset print-boundary viewports')
    return expected

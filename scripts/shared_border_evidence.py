"""Bind intentional shared-border changes to exact PDF paints, content and pixels.

Historical PDFs stay frozen. These separate contracts cover only the reviewed
workbook pages changed by border resolution; unrelated fixtures keep their old
gates. A future missing stroke, extra ink or text change cannot use this review.
"""
from functools import lru_cache
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CONTRACTS = ROOT / 'tools/ExcelRenderer.PdfComparison/shared-border-contracts.json'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def normalized(value):
    if isinstance(value, float):
        return round(value, 3)
    if isinstance(value, (str, int, bool)) or value is None:
        return value
    if isinstance(value, dict):
        return {key: normalized(item) for key, item in value.items() if key not in ('seqno', 'layer')}
    return [normalized(item) for item in value]


def axial_stroke(path):
    return path['type'] == 's' and all(
        item[0] == 'l' and (abs(item[1].x - item[2].x) <= .001 or abs(item[1].y - item[2].y) <= .001)
        for item in path['items'])


def page_snapshot(page):
    content = b''.join(page.parent.xref_stream(xref) for xref in page.get_contents())
    return {
        'contentSha256': digest(content),
        'paintSha256': digest(json.dumps(normalized(page.get_drawings()), sort_keys=True).encode()),
        'rasterSha256': {str(dpi): digest(page.get_pixmap(dpi=dpi, alpha=False).samples) for dpi in (144, 300)},
    }


def independent_checks(before, after):
    errors = []
    def other_paints(page):
        result = []
        for path in page.get_drawings():
            if axial_stroke(path):
                continue
            row = normalized(path)
            # The frozen Core writer reverses a lone filled rectangle's
            # winding. With no holes, both orientations paint the same area.
            if row['type'] == 'f' and len(row['items']) == 1 and row['items'][0][0] == 're':
                row['items'][0][2] = 1
            result.append(row)
        return result

    paints = [other_paints(page) for page in (before, after)]
    if paints[0] != paints[1]:
        errors.append('Non-axial strokes or filled paths changed')
    characters = [[char for span in page.get_texttrace() for char in span['chars']] for page in (before, after)]
    if [char[1] for char in characters[0]] != [char[1] for char in characters[1]]:
        errors.append('Retained glyph IDs changed')
    elif any(max(abs(a - b) for a, b in zip(left[2], right[2])) > .1 for left, right in zip(*characters)):
        errors.append('Glyph origins changed by more than 0.1pt')
    images = [[normalized({key: value.hex() if isinstance(value, bytes) else value for key, value in image.items()
                          if key not in ('number', 'xref')}) for image in page.get_image_info(hashes=True)] for page in (before, after)]
    if images[0] != images[1]:
        errors.append('Image payloads or placement changed')
    return errors


@lru_cache(maxsize=1)
def contracts():
    return json.loads(CONTRACTS.read_text())


def evidence(before, after, family, name, number):
    record = contracts().get(family, {}).get(name, {}).get('pages', {}).get(str(number))
    if record is None:
        return None
    errors = independent_checks(before, after)
    for side, page in (('before', before), ('after', after)):
        content = b''.join(page.parent.xref_stream(xref) for xref in page.get_contents())
        paints = json.dumps(normalized(page.get_drawings()), sort_keys=True).encode()
        if digest(content) != record[side]['contentSha256']:
            errors.append(side + ': decoded content differs from the reviewed border contract')
        if digest(paints) != record[side]['paintSha256']:
            errors.append(side + ': paint geometry differs from the reviewed border contract')
    return {'passed': not errors, 'errors': errors, 'family': family, 'fixture': name, 'page': number,
            'rasterSha256': record['after']['rasterSha256'],
            'reason': 'Exact reviewed shared-border content/paints; unchanged other paints, glyph IDs/origins and images'}


def raster_matches(review, dpi, samples):
    return review is not None and review['passed'] and digest(samples) == review['rasterSha256'].get(str(dpi))


def non_border_commands(lines):
    """Retain viewport transforms/clips while removing only border/line leaves."""
    result = []
    for line in lines:
        if line.startswith(('DrawBorderCommand', 'DrawLineCommand')):
            continue
        if line.startswith('DrawViewportCommand'):
            body = json.loads(line[len('DrawViewportCommand'):])
            body['children'] = non_border_commands(body['children'])
            if not body['children']:
                continue
            result.append(('DrawViewportCommand', body))
        else:
            result.append(line)
    return result

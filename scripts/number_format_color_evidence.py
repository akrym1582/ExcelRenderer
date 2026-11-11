"""Exact, separately recorded named-color corrections; historical contracts stay frozen."""
import hashlib
import json
from pathlib import Path

import fitz

ROOT = Path(__file__).resolve().parents[1]
CONTRACT = ROOT / 'tools/ExcelRenderer.PdfComparison/number-format-color-contracts.json'
BASE = ROOT / 'tests/Shared/LibreOfficeComparisonBaseline'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def contract():
    result = json.loads(CONTRACT.read_text())
    if sha(ROOT / 'tools/ExcelRenderer.PdfComparison/shared-border-contracts.json') != result['historical_border_contract_sha256']:
        raise ValueError('Historical border contract changed')
    if set(result['full']) != {'invoice', 'japanese-grid-report'} or set(result['core']) != {'invoice', 'grid'}:
        raise ValueError('Unexpected named-color review scope')
    return result


def checked_color_pdf(record, before):
    from libreoffice_regression import page_geometry
    after = BASE / record['pdf']
    if sha(before) != record['historical_pdf_sha256'] or sha(after) != record['pdf_sha256']:
        raise ValueError('Reviewed color PDF hash mismatch')
    changes = []
    with fitz.open(before) as a, fitz.open(after) as b:
        if len(a) != len(b):
            raise ValueError('Color correction changed page count')
        for left, right in zip(a, b):
            old, new = page_geometry(left), page_geometry(right)
            if len(old['glyphs']) != len(new['glyphs']):
                raise ValueError('Color correction changed glyph count')
            for x, y in zip(old['glyphs'], new['glyphs']):
                if x != y:
                    changes.append({'text': ''.join(chr(c[0]) for c in x['chars']), 'before': x['color'], 'after': y['color']})
                    if x['color'] != [0, 0, 0] or y['color'] != [1, 0, 0]:
                        raise ValueError('Only the reviewed black-to-red text correction is allowed')
                    x['color'] = y['color']
            if old != new:
                raise ValueError('Color correction changed non-color geometry')
    if changes != record['changes']:
        raise ValueError('Unexpected color-corrected text')
    return after


def full_contracts(historical):
    from shared_border_evidence import page_snapshot
    for name, record in contract()['full'].items():
        before = ROOT / record['historical_pdf']
        after = checked_color_pdf(record, before)
        with fitz.open(before) as a, fitz.open(after) as b:
            for index, (left, right) in enumerate(zip(a, b), 1):
                original = historical['full'][name]['pages'][str(index)]
                if original['after'] != page_snapshot(left) or record['pages'][str(index)]['after'] != page_snapshot(right):
                    raise ValueError('Color follow-up does not bind the reviewed border PDF')
                original['after'] = record['pages'][str(index)]['after']
    return historical

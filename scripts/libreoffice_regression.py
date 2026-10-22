"""Strict Linux regression contracts for the reviewed main/Core PDF outputs.

These are product regression baselines, not Excel golden files. This module never
records/updates expectations and never invokes LibreOffice. Object IDs and PDF
metadata are ignored; page geometry, glyphs, paint, images, links and pixels are not.
"""
import hashlib
import json
from pathlib import Path
import subprocess

import fitz
from shared_border_evidence import normalized

ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / 'tests/Shared/LibreOfficeComparisonBaseline'
MANIFEST = BASE / 'manifest.json'
DPIS = (144, 288)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def page_geometry(page):
    images = [{key: value.hex() if isinstance(value, bytes) else value
               for key, value in item.items() if key not in ('number', 'xref')}
              for item in page.get_image_info(hashes=True)]
    links = [{key: value for key, value in item.items() if key not in ('xref', 'id')}
             for item in page.get_links()]
    return normalized({'mediabox': page.mediabox, 'cropbox': page.cropbox,
                       'rotation': page.rotation, 'text': page.get_text(), 'glyphs': page.get_texttrace(),
                       'paint': page.get_drawings(), 'images': images, 'links': links})


def compare(expected, actual, dpi_values=DPIS, differences=None):
    """Compare independently decoded PDF appearance, without alignment or masking."""
    errors = []
    records = []
    extracted = [subprocess.check_output(['pdftotext', str(path), '-'], text=True) for path in (expected, actual)]
    if extracted[0] != extracted[1]:
        errors.append('Poppler extracted source text changed')
    with fitz.open(expected) as before, fitz.open(actual) as after:
        if len(before) != len(after):
            errors.append(f'Page count: expected {len(before)}, actual {len(after)}')
        for index, (left, right) in enumerate(zip(before, after), 1):
            a, b = page_geometry(left), page_geometry(right)
            differing = [key for key in a if a[key] != b[key]]
            errors.extend(f'Page {index}: {key} changed' for key in differing)
            rasters = {}
            for dpi in dpi_values:
                lp, rp = (page.get_pixmap(dpi=dpi, alpha=False) for page in (left, right))
                same = (lp.width, lp.height, lp.samples) == (rp.width, rp.height, rp.samples)
                rasters[str(dpi)] = same
                if not same:
                    errors.append(f'Page {index}: {dpi}dpi pixels changed')
                    if differences is not None:
                        directory = differences / f'page-{index:03d}-{dpi}dpi'
                        directory.mkdir(parents=True, exist_ok=True)
                        lp.save(directory / 'expected.png')
                        rp.save(directory / 'actual.png')
            records.append({'page': index, 'different_components': differing, 'pixels_equal': rasters})
        return {'passed': not errors, 'errors': errors, 'expected_pages': len(before),
                'actual_pages': len(after), 'poppler_text_equal': extracted[0] == extracted[1], 'pages': records}


def manifest():
    data = json.loads(MANIFEST.read_text())
    reference_path = ROOT / 'docs/validation/libreoffice-reference/manifest.json'
    if sha(reference_path) != data['reference_manifest_sha256']:
        raise ValueError('Reference case manifest changed; review the regression contract explicitly')
    reference = json.loads(reference_path.read_text())
    cases = {case['id']: case for case in data['cases']}
    if len(cases) != 22 or len(data['cases']) != 22 or set(cases) != {c['id'] for c in reference['cases']}:
        raise ValueError('The contract must cover all 22 fixed cases exactly once')
    for case in reference['cases']:
        engines = cases[case['id']]['engines']
        if set(engines) != {'main', 'core'} or any(
                record['input'] != case['input'] or record['input_sha256'] != case['input_sha256']
                for record in engines.values()):
            raise ValueError('Missing engine or changed input contract: ' + case['id'])
    if fitz.VersionBind != data['pymupdf']:
        raise ValueError(f'Use pinned PyMuPDF {data["pymupdf"]}; found {fitz.VersionBind}')
    for font in data['fonts']:
        if sha(ROOT / font['path']) != font['sha256']:
            raise ValueError('Font changed: ' + font['path'])
    return data


def checked_baseline(record):
    for path, digest in [(ROOT / record['input'], record['input_sha256']),
                         (BASE / record['pdf'], record['pdf_sha256'])]:
        if sha(path) != digest:
            raise ValueError('Frozen regression input/baseline changed: ' + str(path))
    return BASE / record['pdf']


def reviewed_core_layout(name, source, historical, actual):
    """Recognize only the three reviewed source-column layout changes.

    Old PDFs/hashes and AA thresholds stay frozen. Exact reviewed current
    geometry/pixels replace the old geometry only for these identified inputs.
    """
    record = manifest()['historical_core'].get(name)
    if record is None:
        return None
    expected = checked_baseline(record)
    if sha(source) != record['input_sha256'] or sha(historical) != record['historical_sha256']:
        raise ValueError('Source/historical Core contract mismatch: ' + name)
    result = compare(expected, actual, (144, 288, 300))
    result['classification'] = 'reviewed-source-column-metrics'
    result['reason'] = record['reason']
    return result

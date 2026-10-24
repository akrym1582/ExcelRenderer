#!/usr/bin/env python3
"""Register Windows Excel evidence and compare it with main/Core, never with Calc."""
import argparse
import collections
import datetime
import importlib.util
import json
from pathlib import Path
import platform
import re
import shutil
import sys
import xml.etree.ElementTree as ET

import fitz
import numpy as np
from PIL import Image, ImageChops, ImageFilter

from shared_border_evidence import normalized, axial_stroke

ROOT = Path(__file__).resolve().parents[1]
REFERENCE = ROOT / 'docs/validation/excel-reference'
CATALOG = REFERENCE / 'cases.json'
KIND = 'microsoft-excel-windows'
DPIS = (144, 288)
spec = importlib.util.spec_from_file_location('calc_comparison', ROOT / 'scripts/compare-libreoffice.py')
common = importlib.util.module_from_spec(spec)
spec.loader.exec_module(common)
sha, save = common.sha, common.save


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def inside(root, relative):
    path = (root / relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError('Path escapes its root: ' + str(relative))
    return path


def cases():
    data = read(CATALOG)
    original = read(ROOT / 'docs/validation/libreoffice-reference/manifest.json')
    expected = {c['id']: (c['input'], c['input_sha256']) for c in original['cases']}
    actual = {c['id']: (c['input'], c['input_sha256']) for c in data['cases']}
    if len(data['cases']) != 22 or actual != expected or data['reference_kind'] != KIND:
        raise ValueError('Excel catalog must cover the existing 22 XLSX inputs exactly once')
    for c in data['cases']:
        if not re.fullmatch(r'[a-zA-Z0-9_-]+', c['id']):
            raise ValueError('Invalid case ID')
        source = inside(ROOT, c['input'])
        if sha(source) != c['input_sha256']:
            raise ValueError('Changed input: ' + c['id'])
        sheets = [s['name'] for s in common.workbook(source)['sheets'] if s.get('state', 'visible') == 'visible']
        if not sheets or c['selected_sheets'] != sheets:
            raise ValueError('Changed visible sheet selection: ' + c['id'])
    return data['cases']


def validate_reference(case, pdf, evidence):
    """Check a producer claim AND operator evidence, not a cryptographic attestation."""
    required = {'reference_kind': KIND, 'case_id': case['id'],
                'input_sha256': case['input_sha256'], 'pdf_sha256': sha(pdf),
                'selected_sheets': case['selected_sheets'], 'export_method': 'ExportAsFixedFormat',
                'quality': 'standard', 'ignore_print_areas': False, 'calculation': 'manual-no-recalculate'}
    for key, value in required.items():
        if key not in evidence or evidence[key] != value:
            raise ValueError('Invalid Excel evidence field: ' + key)
    for key in ('excel_version', 'excel_build', 'windows_version', 'exported_at_utc', 'operator', 'active_printer'):
        if not isinstance(evidence.get(key), str) or not evidence[key].strip():
            raise ValueError('Missing Excel provenance: ' + key)
    stamp = datetime.datetime.fromisoformat(evidence['exported_at_utc'].replace('Z', '+00:00'))
    if stamp.utcoffset() != datetime.timedelta(0):
        raise ValueError('Export timestamp must include a UTC offset')
    fonts = evidence.get('font_files')
    if not isinstance(fonts, list) or not fonts or any(
            not f.get('name') or not re.fullmatch('[0-9a-f]{64}', f.get('sha256', '')) for f in fonts):
        raise ValueError('Record the Windows installed font file inventory and SHA256 hashes')
    with fitz.open(pdf) as doc:
        identity = ' '.join(doc.metadata.get(k, '') or '' for k in ('creator', 'producer'))
        if not len(doc) or doc.needs_pass or not re.search(r'Microsoft.*Excel', identity, re.I):
            raise ValueError('PDF must identify Microsoft Excel and contain readable pages')
        if re.search(r'LibreOffice|OpenOffice|Skia|PDFsharp|ExcelRenderer', identity, re.I):
            raise ValueError('A non-Excel producer cannot be registered as Excel')


def register(bundle, reference, replace=False):
    """Validate the complete supplied subset before changing the registry."""
    catalog = {c['id']: c for c in cases()}
    manifest_path = reference / 'manifest.json'
    manifest = read(manifest_path) if manifest_path.exists() else {'schema_version': 1, 'reference_kind': KIND, 'references': {}}
    validate_registry(manifest, catalog)
    pending = []
    for evidence_path in sorted(bundle.glob('*.json')):
        evidence = read(evidence_path)
        name = evidence.get('case_id')
        if name not in catalog or evidence_path.name != name + '.json':
            raise ValueError('Unknown/misnamed case evidence: ' + evidence_path.name)
        pdf = bundle / (name + '.pdf')
        validate_reference(catalog[name], pdf, evidence)
        if name in manifest['references'] and not replace:
            raise ValueError(name + ' already registered; review and use --replace explicitly')
        pending.append((name, pdf, evidence_path, evidence))
    if not pending:
        raise ValueError('No case evidence JSON found in bundle')
    for name, pdf, evidence_path, evidence in pending:
        # Content-addressed files keep an interrupted registration from corrupting old references.
        pdf_relative = 'pdf/' + name + '-' + sha(pdf) + '.pdf'
        evidence_relative = 'evidence/' + name + '-' + sha(evidence_path) + '.json'
        for source, relative in ((pdf, pdf_relative), (evidence_path, evidence_relative)):
            target = inside(reference, relative)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
        manifest['references'][name] = {'pdf': pdf_relative, 'pdf_sha256': sha(pdf),
                                         'evidence': evidence_relative, 'evidence_sha256': sha(evidence_path)}
    temporary = manifest_path.with_suffix('.tmp')
    save(temporary, manifest)
    temporary.replace(manifest_path)
    print(f'Registered {len(pending)} Windows Excel PDFs; review and commit PDFs, evidence and manifest together')


def validate_registry(data, catalog):
    if data.get('schema_version') != 1 or data.get('reference_kind') != KIND or not isinstance(data.get('references'), dict):
        raise ValueError('Not a Windows Excel reference registry')
    if set(data['references']) - set(catalog):
        raise ValueError('Unknown case in reference registry')
    for entry in data['references'].values():
        if not isinstance(entry, dict):
            raise ValueError('Reference entry must be an object')
        for key in ('pdf', 'evidence'):
            if not isinstance(entry.get(key), str) or not entry[key]:
                raise ValueError('Missing reference path: ' + key)


def reference_pdf(case, reference, entry):
    pdf = inside(reference, entry['pdf'])
    evidence_path = inside(reference, entry['evidence'])
    if sha(pdf) != entry['pdf_sha256'] or sha(evidence_path) != entry['evidence_sha256']:
        raise ValueError('Registered reference/evidence hash mismatch')
    validate_reference(case, pdf, read(evidence_path))
    return pdf


def render(output, catalog):
    dll = ROOT / 'src/ExcelRenderer.Tool/bin/Release/net10.0/ExcelRenderer.Tool.dll'
    environment = {'commit': common.command(['git', 'rev-parse', 'HEAD'])['stdout'].strip(),
                   'os': platform.platform(), 'python': platform.python_version(), 'pymupdf': fitz.VersionBind,
                   'dpi': DPIS, 'reference_kind': KIND,
                   'fonts': {f: sha(ROOT / f) for f in common.FONTS},
                   'runtime_hashes': {p.name: sha(p) for p in dll.parent.glob('ExcelRenderer*.dll')}}
    save(output / 'environment.json', environment)
    errors = []
    for c in catalog:
        for engine in ('main', 'core'):
            pdf = output / 'actual' / engine / (c['id'] + '.pdf')
            record = pdf.with_suffix('.run.json')
            if pdf.exists() or record.exists():
                raise FileExistsError('Use a fresh run directory: ' + str(pdf))
            pdf.parent.mkdir(parents=True, exist_ok=True)
            args = ['dotnet', str(dll), 'render' if engine == 'main' else 'core', c['input'],
                    '--output', str(pdf), '--verbose', '--sheet', *c['selected_sheets']]
            if engine == 'main':
                args += ['--format', 'pdf', '--formula-mode', 'cached', '--font-policy', 'requested',
                         '--no-system-fonts', '--fallback-font', 'Noto Sans JP', '--fallback-font', 'IPAmjMincho',
                         '--layout-warnings', '--detect-unsupported-features',
                         '--manifest', str(pdf.with_suffix('.manifest.json'))]
                for font in common.FONTS:
                    args += ['--font-file', font]
            else:
                args += ['--font-file', common.FONT]
            result = common.command(args)
            result.update(case_id=c['id'], engine=engine, input_sha256=c['input_sha256'], selected_sheets=c['selected_sheets'])
            result['status'] = 'generated' if result['exit_code'] == 0 and pdf.exists() else 'error'
            if result['status'] == 'generated':
                result['pdf_sha256'] = sha(pdf)
            else:
                errors.append(engine + '/' + c['id'])
            save(record, result)
            print(engine, c['id'], result['status'], flush=True)
    return errors


def inspection(pdf):
    """Decoded geometry is diagnostic; object IDs and stream encoding are excluded."""
    pages = []
    with fitz.open(pdf) as document:
        for index, page in enumerate(document):
            chars = []
            for block in page.get_text('rawdict')['blocks']:
                for line in block.get('lines', []):
                    for span in line['spans']:
                        chars.extend({'text': c['c'], 'origin': c['origin'], 'bbox': c['bbox'],
                                      'font': re.sub(r'^[A-Z]{6}\+', '', span['font']), 'size': span['size'], 'color': span['color']}
                                     for c in span['chars'])
            drawings = page.get_drawings()
            images = [{k: v.hex() if isinstance(v, bytes) else v for k, v in item.items()
                       if k not in ('number', 'xref')} for item in page.get_image_info(hashes=True)]
            pages.append(normalized({'page': index + 1, 'text': page.get_text(), 'characters': chars,
                                     'mediabox': page.mediabox, 'cropbox': page.cropbox, 'rotation': page.rotation,
                                     'size': page.rect, 'border_strokes': [p for p in drawings if axial_stroke(p)],
                                     'other_paths': [p for p in drawings if not axial_stroke(p)], 'images': images,
                                     'fonts': sorted({c['font'] for c in chars})}))
    return pages


def raster_difference(left, right, directory, dpi):
    directory.mkdir(parents=True, exist_ok=True)
    originals = [common.raster(p, dpi) if p is not None else None for p in (left, right)]
    size = tuple(max(im.size[i] for im in originals if im is not None) for i in (0, 1))
    canvases = []
    for label, im in zip(('excel', 'actual'), originals):
        canvas = Image.new('RGB', size, 'white')
        if im is not None:
            canvas.paste(im, (0, 0))
        canvas.save(directory / (label + '.png'))
        canvases.append(canvas)
    a, b = canvases
    diff = ImageChops.difference(a, b)
    pixels = np.asarray(diff)
    diff.save(directory / 'difference.png')
    Image.blend(a, b, .5).save(directory / 'overlay.png')
    # Fixed 0.25pt blur, never alignment/dilation or an acceptance mask.
    blurred = np.asarray(ImageChops.difference(a.filter(ImageFilter.GaussianBlur(dpi / 288)),
                                              b.filter(ImageFilter.GaussianBlur(dpi / 288))))
    return {'dpi': dpi, 'canvas_pixels': size, 'changed_pixel_ratio': float(np.any(pixels > 0, axis=2).mean()),
            'over_16_pixel_ratio': float(np.any(pixels > 16, axis=2).mean()),
            'mean_absolute_error': float(pixels.mean()), 'max_channel_difference': int(pixels.max()),
            'blurred_mean_absolute_error': float(blurred.mean()),
            'blur_radius_pt': .25, 'artifact_directory': directory.name}


def compare_pdf(expected, actual, directory):
    left, right = inspection(expected), inspection(actual)
    save(directory / 'excel-inspection.json', left)
    save(directory / 'actual-inspection.json', right)
    positions = common.positions(left, right)
    result = {'expected_sha256': sha(expected), 'actual_sha256': sha(actual),
              'page_count': [len(left), len(right)], 'page_count_equal': len(left) == len(right),
              'document_text': common.text_difference(''.join(p['text'] for p in left), ''.join(p['text'] for p in right)),
              'character_positions': positions, 'pages': [], 'diagnostics': []}
    geometry_equal = len(left) == len(right)
    pixels_equal = True
    font_difference = False
    structure_difference = False
    with fitz.open(expected) as a, fitz.open(actual) as b:
        for i in range(max(len(a), len(b))):
            ap, bp = (a[i] if i < len(a) else None), (b[i] if i < len(b) else None)
            x, y = (left[i] if i < len(left) else {}), (right[i] if i < len(right) else {})
            equal = {k: k in x and k in y and x[k] == y[k]
                     for k in ('size', 'mediabox', 'cropbox', 'rotation', 'characters', 'border_strokes', 'other_paths', 'images', 'fonts')}
            geometry_equal &= all(equal[k] for k in ('size', 'mediabox', 'cropbox', 'rotation'))
            font_difference |= not equal['fonts']
            structure_difference |= not all(equal.values())
            rasters = [raster_difference(ap, bp, directory / f'page-{i + 1:03d}-{dpi}dpi', dpi) for dpi in DPIS]
            pixels_equal &= all(r['changed_pixel_ratio'] == 0 for r in rasters)
            result['pages'].append({'page': i + 1, 'component_equal': equal,
                                    'sizes_pt': [x.get('size'), y.get('size')], 'rasters': rasters})
    # Representation differences alone cannot prove an implementation defect.
    result['status'] = 'matched' if geometry_equal and pixels_equal else 'review-required'
    if font_difference:
        result['diagnostics'].append('font-difference: check installed/embedded fonts and fallback; visible differences remain review-required')
    if structure_difference and pixels_equal:
        result['diagnostics'].append('representation-only-at-tested-dpi: decoded structure differs but rendered pixels agree')
    if not pixels_equal:
        result['diagnostics'].append('visible-difference: blur metrics help investigate antialiasing; no pixels are excused automatically')
    if not geometry_equal:
        result['diagnostics'].append('page-geometry-difference: pages are paired by ordinal; no rescaling or alignment applied')
    result['diagnostics'].append('Root cause is not inferred automatically; compare Core supported scope and Excel calculation/export environment')
    return result


def compare(output, reference, catalog):
    manifest = read(reference / 'manifest.json')
    validate_registry(manifest, {c['id']: c for c in catalog})
    records = []
    for c in catalog:
        for engine in ('main', 'core'):
            record = {'case_id': c['id'], 'engine': engine, 'reference_kind': KIND,
                      'input': c.get('input'), 'input_sha256': c['input_sha256'],
                      'selected_sheets': c['selected_sheets']}
            try:
                entry = manifest['references'].get(c['id'])
                if entry is None:
                    record.update(status='unverified', reason='Windows Excel reference PDF has not been registered')
                else:
                    expected = reference_pdf(c, reference, entry)
                    pdf = output / 'actual' / engine / (c['id'] + '.pdf')
                    execution = read(pdf.with_suffix('.run.json'))
                    for key, value in {'status': 'generated', 'case_id': c['id'], 'engine': engine,
                                       'input_sha256': c['input_sha256'], 'selected_sheets': c['selected_sheets'],
                                       'pdf_sha256': sha(pdf)}.items():
                        if execution.get(key) != value:
                            raise ValueError('Actual output provenance mismatch: ' + key)
                    directory = output / 'comparisons' / engine / c['id']
                    record.update(compare_pdf(expected, pdf, directory))
                    record['artifact_directory'] = str(directory.relative_to(output))
                    evidence_path = inside(reference, entry['evidence'])
                    save(directory / 'excel-evidence.json', read(evidence_path))
                    record['excel_evidence_sha256'] = sha(evidence_path)
            except (ValueError, KeyError, OSError, RuntimeError) as error:
                record.update(status='error', reason=str(error))
            records.append(record)
            print(engine, c['id'], record['status'], flush=True)
    summary = dict(collections.Counter(r['status'] for r in records))
    generation_errors = []
    for c in catalog:
        for engine in ('main', 'core'):
            execution = output / 'actual' / engine / (c['id'] + '.run.json')
            if execution.exists() and read(execution).get('status') != 'generated':
                generation_errors.append(engine + '/' + c['id'])
    report = {'schema_version': 1, 'reference_kind': KIND, 'dpi': DPIS, 'summary': summary, 'records': records, 'generation_errors': generation_errors,
              'coverage_complete': all(r['status'] in ('matched', 'review-required') for r in records),
              'fidelity_passed': not generation_errors and all(r['status'] == 'matched' for r in records)}
    save(output / 'report.json', report)
    suite = ET.Element('testsuite', name='Windows Excel appearance', tests=str(len(records)),
                       failures=str(summary.get('review-required', 0)), errors=str(summary.get('error', 0)),
                       skipped=str(summary.get('unverified', 0)))
    for r in records:
        test = ET.SubElement(suite, 'testcase', classname=r['engine'], name=r['case_id'])
        if r['status'] != 'matched':
            tag = {'unverified': 'skipped', 'review-required': 'failure', 'error': 'error'}[r['status']]
            ET.SubElement(test, tag, message=r.get('reason', 'Visible difference; inspect report and PNGs'))
    ET.ElementTree(suite).write(output / 'junit.xml', encoding='utf-8', xml_declaration=True)
    lines = ['# Windows Excel comparison', '', 'Excel fidelity passed: **' + str(report['fidelity_passed']) + '**', '',
             '| Status | Pairs |', '| --- | ---: |'] + [f'| {k} | {v} |' for k, v in sorted(summary.items())]
    lines += ['', 'Generation errors: ' + str(len(generation_errors)), '', 'Unregistered references are UNVERIFIED, never a fidelity pass. See report.json, junit.xml and comparisons/ for evidence.']
    (output / 'summary.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    registration = sub.add_parser('register')
    registration.add_argument('bundle', type=Path)
    registration.add_argument('--reference', type=Path, default=REFERENCE)
    registration.add_argument('--replace', action='store_true')
    run = sub.add_parser('run')
    run.add_argument('--output', type=Path, default=ROOT / 'artifacts/excel-reference')
    run.add_argument('--reference', type=Path, default=REFERENCE)
    run.add_argument('--no-generate', action='store_true')
    run.add_argument('--fail-on-difference', action='store_true')
    run.add_argument('--require-all-references', action='store_true')
    args = parser.parse_args()
    if args.action == 'register':
        register(args.bundle.resolve(), args.reference.resolve(), args.replace)
        return 0
    catalog = cases()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    failures = [] if args.no_generate else render(output, catalog)
    report = compare(output, args.reference.resolve(), catalog)
    counts = report['summary']
    return int(bool(failures or report['generation_errors'] or counts.get('error') or
                    (args.fail_on_difference and counts.get('review-required')) or
                    (args.require_all_references and counts.get('unverified'))))


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, KeyError, OSError, RuntimeError) as error:
        print('Excel validation error: ' + str(error), file=sys.stderr)
        sys.exit(2)

#!/usr/bin/env python3
"""Independent provisional Calc/main/Core comparison; never updates reference PDFs.

Requires PyMuPDF, Pillow and numpy. Source scripts/test-fonts.sh before rendering.
All images retain page coordinates and scale. Text differences are review signals,
not automatic findings of product defects or Excel compatibility.
"""
import argparse
import collections
import difflib
import hashlib
import html
import json
import platform
import subprocess
import time
import unicodedata
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile

import fitz
import numpy as np
from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[1]
REFERENCE = ROOT / 'docs/validation/libreoffice-reference'
MANIFEST = REFERENCE / 'manifest.json'
FONT = 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
FONTS = [FONT, 'third_party/IPAmjMincho/ipamjm.ttf',
         'tests/ExcelRenderer.Tests/Fonts/NotoSansMono-Regular.ttf']
PAIRS = [('libreoffice-main', 'libreoffice', 'main'),
         ('libreoffice-core', 'libreoffice', 'core'), ('main-core', 'main', 'core')]
NS = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def command(args):
    p = subprocess.run(args, cwd=ROOT, capture_output=True, text=True)
    return {'command': args, 'exit_code': p.returncode, 'stdout': p.stdout, 'stderr': p.stderr}


def workbook(path):
    with ZipFile(path) as z:
        tree = ET.fromstring(z.read('xl/workbook.xml'))
        sheets = [s.attrib for s in tree.findall('s:sheets/s:sheet', NS)]
        features = []
        for name in z.namelist():
            if name.startswith('xl/worksheets/') and name.endswith('.xml'):
                t = ET.fromstring(z.read(name))
                cf = [x for x in t.iter() if x.tag.split('}')[-1] == 'conditionalFormatting']
                formulas = t.findall('.//s:c[s:f]', NS)
                features.append({'part': name,
                                 'conditional_ranges': [x.get('sqref') or ' '.join(x.itertext()) for x in cf],
                                 'formulas': [{'cell': c.get('r'), 'formula': c.find('s:f', NS).text,
                                               'cache': c.findtext('s:v', None, NS)} for c in formulas],
                                 'print_settings': {k: [x.attrib for x in t.findall('s:' + k, NS)]
                                                    for k in ['pageSetup', 'pageMargins', 'rowBreaks', 'colBreaks']}})
        return {'sheets': sheets, 'worksheet_metadata': features,
                'drawing_parts': [n for n in z.namelist() if n.startswith('xl/drawings/') and n.endswith('.xml')]}


def validate_inputs(manifest):
    records = []
    for c in manifest['cases']:
        item = {'case_id': c['id']}
        for key, path in [('input', ROOT / c['input']), ('pdf', REFERENCE / c['pdf'])]:
            actual = sha(path)
            item[key] = {'path': str(path.relative_to(ROOT)), 'expected': c[key + '_sha256'],
                         'actual': actual, 'matches': actual == c[key + '_sha256']}
        records.append(item)
    return records


def render(run, manifest):
    checks = validate_inputs(manifest)
    save(run / 'input-reference-hashes.json', checks)
    save(run / 'environment.json', {
        'commit': command(['git', 'rev-parse', 'HEAD'])['stdout'].strip(),
        'os': platform.platform(), 'dotnet': command(['dotnet', '--info']),
        'rasterizer': 'PyMuPDF/MuPDF ' + fitz.VersionBind, 'dpi': [144, 288],
        'fonts': [{'path': f, 'sha256': sha(ROOT / f)} for f in FONTS],
        'runtime_hashes': {p.name: sha(p) for p in
                          (ROOT / 'src/ExcelRenderer.Tool/bin/Release/net10.0').glob('ExcelRenderer*.dll')},
        'reference_kind': manifest['reference_kind'],
    })
    dll = ROOT / 'src/ExcelRenderer.Tool/bin/Release/net10.0/ExcelRenderer.Tool.dll'
    for c, hashes in zip(manifest['cases'], checks):
        metadata = workbook(ROOT / c['input'])
        sheets = [s['name'] for s in metadata['sheets'] if s.get('state', 'visible') == 'visible']
        for engine in ['main', 'core']:
            out = run / 'actual' / engine / (c['id'] + '.pdf')
            record = out.with_suffix('.run.json')
            if record.exists() or out.exists():
                raise FileExistsError(f'Use a fresh run directory: {out}')
            out.parent.mkdir(parents=True, exist_ok=True)
            result = {'case_id': c['id'], 'engine': engine, 'input': c['input'],
                      'input_sha256': hashes['input']['actual'], 'hashes_valid': all(hashes[k]['matches'] for k in ['input', 'pdf']),
                      'selected_sheets': sheets, 'workbook': metadata,
                      'font_hashes': {f: sha(ROOT / f) for f in (FONTS if engine == 'main' else [FONT])}}
            if not result['hashes_valid'] or not sheets:
                result.update(status='unverified', reason='Input/reference hash mismatch or no visible sheets')
            else:
                args = ['dotnet', str(dll), 'render' if engine == 'main' else 'core', c['input'],
                        '--output', str(out), '--verbose', '--sheet', *sheets]
                if engine == 'main':
                    args += ['--format', 'pdf', '--formula-mode', 'cached', '--font-policy', 'requested',
                             '--no-system-fonts', '--fallback-font', 'Noto Sans JP', '--fallback-font', 'IPAmjMincho',
                             '--layout-warnings', '--detect-unsupported-features',
                             '--manifest', str(out.with_suffix('.manifest.json'))]
                    for f in FONTS:
                        args += ['--font-file', f]
                else:
                    args += ['--font-file', FONT]
                start = time.monotonic()
                result.update(command(args))
                result['elapsed_seconds'] = time.monotonic() - start
                result['status'] = 'generated' if result['exit_code'] == 0 and out.exists() else 'failed'
                if out.exists():
                    result['pdf_sha256'] = sha(out)
                    with fitz.open(out) as pdf:
                        result['page_count'] = len(pdf)
            save(record, result)
            print(engine, c['id'], result['status'], result.get('page_count'), flush=True)


def inspect_pdf(path, target):
    with fitz.open(path) as pdf:
        embedded_fonts = []
        seen = set()
        for page in pdf:
            for font in page.get_fonts(full=True):
                if font[0] in seen:
                    continue
                seen.add(font[0])
                name, extension, kind, data = pdf.extract_font(font[0])
                embedded_fonts.append({'xref': font[0], 'name': name, 'extension': extension,
                                       'kind': kind, 'bytes': len(data),
                                       'sha256': hashlib.sha256(data).hexdigest() if data else None})
        pages = []
        for index, page in enumerate(pdf):
            chars = []
            for block in page.get_text('rawdict')['blocks']:
                for line in block.get('lines', []):
                    for span in line['spans']:
                        chars.extend({'text': c['c'], 'bbox': c['bbox'], 'origin': c['origin'],
                                      'font': span['font'], 'size': span['size'], 'color': span['color']}
                                     for c in span['chars'])
            pages.append({'page': index + 1, 'width_pt': page.rect.width, 'height_pt': page.rect.height,
                          'text': page.get_text(), 'characters': chars,
                          'fonts': page.get_fonts(full=True),
                          'drawings': page.get_drawings(), 'images': page.get_images(full=True)})
        # Drawing Points/Rects are converted to simple lists for JSON serialization.
        def serial(value):
            if isinstance(value, (fitz.Point, fitz.Rect, fitz.Quad)):
                return list(value)
            raise TypeError(type(value).__name__)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps({'path': str(path.relative_to(ROOT)), 'sha256': sha(path),
                                     'embedded_fonts': embedded_fonts, 'pages': pages}, ensure_ascii=False, indent=2, default=serial) + '\n')
        return pages


def counts(text):
    return collections.Counter(c for c in unicodedata.normalize('NFC', text) if not c.isspace())


def text_difference(left, right):
    a, b = counts(left), counts(right)
    return {'left_only_characters': dict(a - b), 'right_only_characters': dict(b - a),
            'same_non_whitespace_character_inventory': a == b,
            'same_extracted_text': left == right}


def raster(page, dpi):
    pix = page.get_pixmap(matrix=fitz.Matrix(dpi / 72, dpi / 72), colorspace=fitz.csRGB, alpha=False)
    return Image.frombytes('RGB', [pix.width, pix.height], pix.samples)


def positions(left, right):
    """Align matching extraction characters without claiming source-cell identity."""
    def flatten(pages):
        return [dict(c, page=p['page']) for p in pages for c in p['characters'] if not c['text'].isspace()]
    a, b = flatten(left), flatten(right)
    matcher = difflib.SequenceMatcher(None, [c['text'] for c in a], [c['text'] for c in b], autojunk=False)
    matched = []
    for block in matcher.get_matching_blocks():
        for offset in range(block.size):
            x, y = a[block.a + offset], b[block.b + offset]
            matched.append({'text': x['text'], 'left_page': x['page'], 'right_page': y['page'],
                            'left_origin': x['origin'], 'right_origin': y['origin'],
                            'left_bbox': x['bbox'], 'right_bbox': y['bbox'],
                            'dx_pt': y['origin'][0] - x['origin'][0], 'dy_pt': y['origin'][1] - x['origin'][1]})
    return {'method': 'Extraction sequence alignment; repeated characters and extraction order can be ambiguous',
            'matched_characters': len(matched), 'matched_characters_on_different_pages':
            sum(c['left_page'] != c['right_page'] for c in matched), 'matches': matched}


def images(left, right, directory, dpi):
    directory.mkdir(parents=True, exist_ok=True)
    a, b = raster(left, dpi) if left else None, raster(right, dpi) if right else None
    size = (max(x.width for x in [a, b] if x), max(x.height for x in [a, b] if x))
    canvases = []
    for label, im in [('left', a), ('right', b)]:
        canvas = Image.new('RGB', size, 'white')
        if im:
            im.save(directory / (label + '.png'))
            canvas.paste(im, (0, 0))
        canvases.append(canvas)
    a, b = canvases
    diff = ImageChops.difference(a, b)
    changed = np.any(np.asarray(diff) > 0, axis=2)
    substantial = np.any(np.asarray(diff) > 16, axis=2)
    diff.save(directory / 'difference.png')
    Image.blend(a, b, .5).save(directory / 'overlay.png')
    side = Image.new('RGB', (size[0] * 2, size[1]), 'white')
    side.paste(a, (0, 0))
    side.paste(b, (size[0], 0))
    side.save(directory / 'side-by-side.png')
    return {'dpi': dpi, 'canvas_pixels': size, 'changed_pixel_ratio': float(changed.mean()),
            'difference_over_16_ratio': float(substantial.mean()),
            'side_by_side': str((directory / 'side-by-side.png').relative_to(ROOT))}


def compare_pair(case_id, pair, left_path, right_path, left_info, right_info, run):
    record = {'case_id': case_id, 'pair': pair, 'status': 'compared-needs-review',
              'left_sha256': sha(left_path), 'right_sha256': sha(right_path),
              'page_count': [len(left_info), len(right_info)],
              'page_count_equal': len(left_info) == len(right_info),
              'page_correspondence': 'ordinal only; if counts differ consult both complete page sets',
              'document_text': text_difference(''.join(p['text'] for p in left_info),
                                                ''.join(p['text'] for p in right_info)),
              'character_positions': positions(left_info, right_info), 'pages': []}
    with fitz.open(left_path) as a, fitz.open(right_path) as b:
        for i in range(max(len(a), len(b))):
            ap, bp = a[i] if i < len(a) else None, b[i] if i < len(b) else None
            page = {'page': i + 1, 'sizes_pt': [list(ap.rect) if ap else None, list(bp.rect) if bp else None],
                    'size_equal': ap is not None and bp is not None and tuple(ap.rect) == tuple(bp.rect),
                    'text': text_difference(ap.get_text() if ap else '', bp.get_text() if bp else ''),
                    'rasters': []}
            resolutions = [144, 288] if ('border' in case_id or case_id.startswith('cf-')) else [144]
            for dpi in resolutions:
                directory = run / 'images' / pair / case_id / f'page-{i + 1:03d}-{dpi}dpi'
                page['rasters'].append(images(ap, bp, directory, dpi))
            record['pages'].append(page)
    return record


def compare(run, manifest, report):
    report.mkdir(parents=True, exist_ok=True)
    records = []
    table = []
    for c in manifest['cases']:
        paths = {'libreoffice': REFERENCE / c['pdf'],
                 **{e: run / 'actual' / e / (c['id'] + '.pdf') for e in ['main', 'core']}}
        infos = {}
        for engine, path in paths.items():
            execution = run / 'actual' / engine / (c['id'] + '.run.json')
            if path.exists() and (engine == 'libreoffice' or
                                  (execution.exists() and json.loads(execution.read_text())['status'] == 'generated')):
                infos[engine] = inspect_pdf(path, run / 'pdf-inspection' / engine / (c['id'] + '.json'))
        for pair, left, right in PAIRS:
            if left not in infos or right not in infos:
                record = {'case_id': c['id'], 'pair': pair, 'status': 'unverified', 'reason': 'generation failed or not executed'}
            else:
                record = compare_pair(c['id'], pair, paths[left], paths[right], infos[left], infos[right], run)
            save(report / 'pairs' / pair / (c['id'] + '.json'), record)
            records.append(record)
            table.append(f'<tr><td>{html.escape(c["id"])}</td><td>{pair}</td><td>{record.get("page_count")}</td>'
                         f'<td>{record.get("document_text", {}).get("same_non_whitespace_character_inventory")}</td><td>'
                         + ' '.join(f'<a href="{html.escape(str((ROOT / r["side_by_side"]).relative_to(ROOT)))}">p{p["page"]} {r["dpi"]}dpi</a>'
                                    for p in record.get('pages', []) for r in p['rasters']) + '</td></tr>')
            print(pair, c['id'], record.get('page_count'), flush=True)
    summary = {'engines': {}, 'pairs': {}}
    for engine in ['main', 'core']:
        runs = [json.loads((run / 'actual' / engine / (c['id'] + '.run.json')).read_text()) for c in manifest['cases']]
        save(report / (engine + '-cases.json'), runs)
        summary['engines'][engine] = {'cases': len(runs), 'generated_pdfs': sum(r['status'] == 'generated' for r in runs),
                                      'pages': sum(r.get('page_count', 0) for r in runs),
                                      'failed_or_unverified': sum(r['status'] != 'generated' for r in runs)}
    for pair, _, _ in PAIRS:
        items = [r for r in records if r['pair'] == pair]
        summary['pairs'][pair] = {'records': len(items), 'compared': sum(r['status'].startswith('compared') for r in items),
                                  'equal_page_counts': sum(r.get('page_count_equal', False) for r in items),
                                  'equal_text_inventory': sum(r.get('document_text', {}).get('same_non_whitespace_character_inventory', False) for r in items)}
    save(report / 'summary.json', summary)
    # Root-relative URLs work when serving the checkout root with python -m http.server.
    (report / 'index.html').write_text('<!doctype html><meta charset="utf-8"><base href="/">'
                                      '<title>Provisional Calc comparison</title><h1>Calc / main / Core</h1>'
                                      '<p>Review signals only. Left/right scale and coordinates preserved. '
                                      'Side-by-side links have overlay.png and difference.png siblings. '
                                      'Page count mismatches have no established correspondence.</p>'
                                      '<table border="1"><tr><th>Case</th><th>Pair (left-right)</th><th>Pages</th>'
                                      '<th>Text inventory equal</th><th>All pages</th></tr>' + ''.join(table) + '</table>')
    for name in ['environment.json', 'input-reference-hashes.json']:
        save(report / name, json.loads((run / name).read_text()))
    print(json.dumps(summary, indent=2))


def self_test(directory):
    """Independent synthetic mutations exercise text, line and page detection."""
    directory.mkdir(parents=True, exist_ok=True)
    def fixture(path, text=True, line=True, extra=False):
        with fitz.open() as d:
            p = d.new_page(width=200, height=200)
            if text:
                p.insert_text((20, 40), 'MISSING_SENTINEL_123')
            if line:
                p.draw_line((20, 70), (180, 70), width=2)
            if extra:
                d.new_page(width=200, height=200)
            d.save(path)
    base = directory / 'baseline.pdf'
    fixture(base)
    a = inspect_pdf(base, directory / 'baseline.json')
    results = []
    for name, options in [('missing-text', {'text': False}), ('missing-line', {'line': False}), ('extra-page', {'extra': True})]:
        path = directory / (name + '.pdf')
        fixture(path, **options)
        b = inspect_pdf(path, directory / (name + '.json'))
        delta = text_difference(''.join(p['text'] for p in a), ''.join(p['text'] for p in b))
        with fitz.open(base) as x, fitz.open(path) as y:
            pixels = images(x[0], y[0], directory / name, 144)
        detected = (bool(delta['left_only_characters']) if name == 'missing-text' else
                    pixels['changed_pixel_ratio'] > 0 and len(a[0]['drawings']) > len(b[0]['drawings']) if name == 'missing-line' else
                    len(a) != len(b))
        assert detected, name
        results.append({'mutation': name, 'detected': detected, 'text': delta, 'raster': pixels})
    save(directory / 'results.json', results)
    print('Mutation self-tests passed: missing text, missing line, extra page')


def investigate(run):
    """Keep recalculation/metric experiments separate from the primary cached PDFs."""
    directory = run / 'investigation'
    directory.mkdir(parents=True, exist_ok=True)
    dll = str(ROOT / 'src/ExcelRenderer.Tool/bin/Release/net10.0/ExcelRenderer.Tool.dll')
    records = []
    for case_id, extra in [('08-print-scaling', ['--formula-mode', 'recalculate']),
                           ('01-japanese', ['--layout-font', FONT]),
                           ('02-image', ['--layout-font', FONT]),
                           ('08-print-scaling', ['--layout-font', FONT])]:
        name = case_id + ('-recalculate' if 'recalculate' in extra else '-source-font')
        path = directory / (name + '.pdf')
        if path.exists():
            raise FileExistsError(path)
        args = ['dotnet', dll, 'render', f'tests/ExcelRenderer.Tests/SampleInputs/{case_id}.xlsx',
                '--format', 'pdf', '--output', str(path), '--font-policy', 'requested',
                '--no-system-fonts', '--font-file', FONT, '--fallback-font', 'Noto Sans JP',
                '--layout-warnings', '--verbose', *extra]
        if 'recalculate' not in extra:
            args += ['--formula-mode', 'cached']
        r = command(args)
        r.update(experiment=name, page_count=None)
        if r['exit_code'] == 0:
            info = inspect_pdf(path, directory / (name + '.inspection.json'))
            r.update(page_count=len(info), text=''.join(p['text'] for p in info), pdf_sha256=sha(path))
        records.append(r)
    # A task-created copy is the only workbook changed for this contract probe.
    original = ROOT / 'tests/ExcelRenderer.Tests/SampleInputs/07-multiple-sheets.xlsx'
    hidden = directory / 'hidden-sheet-copy.xlsx'
    from zipfile import ZIP_DEFLATED
    with ZipFile(original) as source, ZipFile(hidden, 'w', ZIP_DEFLATED) as dest:
        for member in source.infolist():
            data = source.read(member.filename)
            if member.filename == 'xl/workbook.xml':
                tree = ET.fromstring(data)
                tree.findall('s:sheets/s:sheet', NS)[1].set('state', 'hidden')
                data = ET.tostring(tree)
            dest.writestr(member, data)
    for label, options in [('all', []), ('visible', ['--sheet', '売上'])]:
        path = directory / ('hidden-' + label + '.pdf')
        if path.exists():
            raise FileExistsError(path)
        r = command(['dotnet', dll, 'core', str(hidden), '--output', str(path), '--font-file', FONT, '--verbose', *options])
        with fitz.open(path) as pdf:
            r.update(experiment='hidden-sheet-' + label, page_count=len(pdf),
                     text=''.join(p.get_text() for p in pdf), input_sha256=sha(hidden), pdf_sha256=sha(path))
        assert r['exit_code'] == 0 and r['page_count'] == (2 if label == 'all' else 1)
        records.append(r)
    save(directory / 'experiments.json', records)
    print([(r['experiment'], r['exit_code'], r['page_count']) for r in records])


def review(run, report):
    """Attach retained human findings without turning generation into a fidelity pass."""
    findings = json.loads((report / 'findings.json').read_text())
    summary = json.loads((report / 'summary.json').read_text())
    for engine in ['main', 'core']:
        executions = json.loads((report / (engine + '-cases.json')).read_text())
        verdicts = []
        for execution in executions:
            selected = [f for f in findings if execution['case_id'] in f['cases'] and engine in f['engines']]
            verdicts.append({'case_id': execution['case_id'], 'engine': engine,
                             'generation': execution['status'], 'page_count': execution.get('page_count'),
                             'verdict': 'unverified' if execution['status'] != 'generated' else
                             'review-required' if any(f['category'] in ['excel-review', 'contract-discrepancy'] for f in selected) else
                             'observed-with-classified-differences' if selected else 'no-material-difference-observed',
                             'findings': [f['id'] for f in selected],
                             'common_scope_reviewed': 'Values, ordinary formatting, borders and pagination; see pair evidence and findings. No desktop Excel check.',
                             'input_sha256': execution['input_sha256'], 'pdf_sha256': execution.get('pdf_sha256')})
        save(report / (engine + '-verdicts.json'), verdicts)
        summary['engines'][engine]['core_contract_difference_cases'] = sum(
            any(f['category'] == 'core-contract' and engine in f['engines'] and v['case_id'] in f['cases'] for f in findings) for v in verdicts)
        summary['engines'][engine]['unresolved_contract_cases'] = sum(
            any(f['category'] == 'contract-discrepancy' and engine in f['engines'] and v['case_id'] in f['cases'] for f in findings) for v in verdicts)
    gallery = ['# Comparison previews\n\nThese are reduced browsing previews. Full, unscaled 144/288 dpi images, '
               'overlays, differences and each original page remain under `artifacts/libreoffice-comparison/images/`. '
               'Left/right engine names follow the pair name. Page pairing is ordinal and is not established when counts differ.\n']
    for pair, _, _ in PAIRS:
        for path in sorted((report / 'pairs' / pair).glob('*.json')):
            record = json.loads(path.read_text())
            record['findings'] = [f['id'] for f in findings if record['case_id'] in f['cases'] and pair in f['pairs']]
            save(path, record)
            gallery.append(f'## {pair} / {record["case_id"]}\n')
            for page in record.get('pages', []):
                source = ROOT / page['rasters'][0]['side_by_side']
                preview = report / 'previews' / pair / record['case_id'] / f'page-{page["page"]:03d}.png'
                preview.parent.mkdir(parents=True, exist_ok=True)
                with Image.open(source) as original:
                    original.thumbnail((1000, 720))
                    original.save(preview)
                gallery.append(f'Page {page["page"]}: ![comparison preview]({preview.relative_to(report).as_posix()})\n')
    (report / 'gallery.md').write_text('\n'.join(gallery))
    save(report / 'summary.json', summary)
    save(report / 'mutation-self-tests.json', json.loads((run / 'self-test/results.json').read_text()))
    save(report / 'reader-isolation.json', json.loads((run / 'reader-isolation.json').read_text()))
    save(report / 'investigations.json', json.loads((run / 'investigation/experiments.json').read_text()))
    artifacts = {str(p.relative_to(ROOT)): sha(p) for p in sorted(run.rglob('*'))
                 if p.is_file() and 'isolation-consumers' not in p.relative_to(run).parts}
    save(report / 'run-artifact-hashes.json', artifacts)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['render', 'compare', 'self-test', 'investigate', 'review'])
    parser.add_argument('--run', type=Path, default=ROOT / 'artifacts/libreoffice-comparison')
    parser.add_argument('--report', type=Path, default=ROOT / 'docs/validation/libreoffice-comparison')
    args = parser.parse_args()
    manifest = json.loads(MANIFEST.read_text())
    if args.action == 'render':
        render(args.run.resolve(), manifest)
    elif args.action == 'compare':
        compare(args.run.resolve(), manifest, args.report.resolve())
    elif args.action == 'self-test':
        self_test(args.run.resolve() / 'self-test')
    elif args.action == 'investigate':
        investigate(args.run.resolve())
    else:
        review(args.run.resolve(), args.report.resolve())


if __name__ == '__main__':
    main()

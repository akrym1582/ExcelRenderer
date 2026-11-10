#!/usr/bin/env python3
"""Compare repository reports and explicit stress fixtures before/after Mapping; never alter source reports."""
import argparse
from collections import Counter
import json
import runpy
from pathlib import Path
import shutil
import subprocess
import unicodedata
import xml.etree.ElementTree as ET
from zipfile import ZipFile, ZIP_DEFLATED

import fitz
from PIL import Image, ImageDraw
from libreoffice_regression import ROOT, sha
workbook = runpy.run_path(str(ROOT / 'scripts/compare-libreoffice.py'))['workbook']

NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
D = 'http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing'
CLI = ROOT / 'src/ExcelRenderer.Tool/bin/Release/net10.0/ExcelRenderer.Tool.dll'
FONT = ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'


def run(args, log):
    result = subprocess.run([str(a) for a in args], cwd=ROOT, capture_output=True, text=True)
    record = {'command': [str(a) for a in args], 'exit_code': result.returncode,
              'stdout': result.stdout, 'stderr': result.stderr}
    log.append(record)
    if result.returncode:
        raise RuntimeError(record)


def placeholder(source, target, cell_address, crossing=False):
    with ZipFile(source) as archive:
        entries = {name: archive.read(name) for name in archive.namelist()}
    sheet = ET.fromstring(entries['xl/worksheets/sheet1.xml'])
    cell = next(c for c in sheet.iter('{' + NS + '}c') if c.get('r') == cell_address)
    for child in list(cell):
        cell.remove(child)
    cell.set('t', 'inlineStr')
    ET.SubElement(ET.SubElement(cell, '{' + NS + '}is'), '{' + NS + '}t').text = '**value'
    if crossing:
        # Deliberate stress fixture: ordinary rows, source 100% scale/margins/paper,
        # and a 900pt image anchored at row 31, spanning natural page boundaries.
        data = sheet.find('{' + NS + '}sheetData')
        for row_number in range(4, 91):
            ET.SubElement(data, '{' + NS + '}row', {'r': str(row_number), 'ht': '15', 'customHeight': '1'})
        sheet.find('{' + NS + '}dimension').set('ref', 'A1:B90')
        drawing = ET.fromstring(entries['xl/drawings/drawing1.xml'])
        for origin in drawing.iter('{' + D + '}from'):
            origin.find('{' + D + '}row').text = '30'
        for extent in drawing.iter():
            if extent.tag.endswith('}ext') and 'cy' in extent.attrib:
                extent.set('cy', str(900 * 12700))
        entries['xl/drawings/drawing1.xml'] = ET.tostring(drawing, encoding='utf-8', xml_declaration=True)
    entries['xl/worksheets/sheet1.xml'] = ET.tostring(sheet, encoding='utf-8', xml_declaration=True)
    with ZipFile(target, 'w', ZIP_DEFLATED) as archive:
        for name, data in entries.items():
            archive.writestr(name, data)


def inspect(path):
    pages = []
    with fitz.open(path) as document:
        for page in document:
            spans = [{'text': span['text'], 'bbox': list(span['bbox'])} for block in page.get_text('dict')['blocks'] if 'lines' in block for line in block['lines'] for span in line['spans']]
            drawings = [{'rect': list(d['rect']), 'type': d['type'], 'width': d['width'], 'color': d['color']} for d in page.get_drawings()]
            images = [{k: v for k, v in i.items() if k in ('bbox', 'width', 'height', 'transform')} for i in page.get_image_info()]
            pages.append({'page': page.number + 1, 'text': page.get_text(), 'spans': spans, 'drawings': drawings, 'images': images})
    return {'sha256': sha(path), 'page_count': len(pages), 'pages': pages}


def normalized(text):
    return ''.join(unicodedata.normalize('NFKC', text).split())


def compare(left, right):
    a = Counter(normalized(''.join(p['text'] for p in left['pages'])))
    b = Counter(normalized(''.join(p['text'] for p in right['pages'])))
    positions = []
    for index, (lp, rp) in enumerate(zip(left['pages'], right['pages']), 1):
        available = list(rp['spans'])
        for span in lp['spans']:
            candidate = next((s for s in available if normalized(s['text']) == normalized(span['text'])), None)
            if candidate is not None:
                positions.append({'page': index, 'text': span['text'], 'dx_pt': round(candidate['bbox'][0] - span['bbox'][0], 3), 'dy_pt': round(candidate['bbox'][1] - span['bbox'][1], 3)})
                available.remove(candidate)
    return {'left_pages': left['page_count'], 'right_pages': right['page_count'],
            'characters_only_left': dict(a - b), 'characters_only_right': dict(b - a),
            'matched_span_positions': positions,
            'image_counts': [[len(p['images']) for p in side['pages']] for side in (left, right)],
            'drawing_counts': [[len(p['drawings']) for p in side['pages']] for side in (left, right)]}


def contact(paths, destination):
    docs = [fitz.open(p) if p is not None else None for p in paths]
    rows = max((len(d) for d in docs if d is not None), default=1)
    canvas = Image.new('RGB', (1200, 590 * rows), '#dddddd')
    draw = ImageDraw.Draw(canvas)
    for col, (name, doc) in enumerate(zip(['main', 'core', 'LibreOffice 25.2'], docs)):
        if doc is None:
            draw.text((col * 400 + 8, 5), name + ': PivotSavedValuesMissing; no PDF', fill='black')
            continue
        for index, page in enumerate(doc):
            pix = page.get_pixmap(matrix=fitz.Matrix(.63, .63))
            img = Image.frombytes('RGB', (pix.width, pix.height), pix.samples)
            img.thumbnail((390, 560))
            canvas.paste(img, (col * 400, index * 590 + 25))
            draw.text((col * 400 + 8, index * 590 + 5), name + f' page {index+1}', fill='black')
        doc.close()
    canvas.save(destination)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--soffice', type=Path, required=True)
    parser.add_argument('--run', type=Path, default=ROOT / 'artifacts/pr113-forms')
    args = parser.parse_args()
    output = args.run.resolve()
    if output.exists():
        raise FileExistsError('Use a fresh evidence directory')
    output.mkdir(parents=True)
    log, cases, report = [], [], {'scope': 'repository invoice examples and synthetic stress fixtures; user-provided real reports unavailable in this checkout', 'cases': []}
    for name, sub in [('invoice-repeat', 'samples/mapping/invoice'), ('invoice-manual', 'samples/mapping/invoice/page-breaks')]:
        folder = output / name
        folder.mkdir()
        source = ROOT / sub / 'template.xlsx'
        before = folder / 'before.xlsx'
        shutil.copyfile(source, before)
        after = folder / 'after.xlsx'
        run(['dotnet', CLI, 'xlsx', before, '--data', ROOT / sub / 'data.json', '-o', after], log)
        cases.append((name, before, after, 'repository invoice sample'))
    for name, sample, address, value, crossing in [
        ('wrapped', '03-wrapped-text', 'A2', '差し込み確認。長文の折り返しを検証します。' * 5, False),
        ('crossing-images', '02-image', 'A2', '画像差し込み確認', True),
    ]:
        folder = output / name
        folder.mkdir()
        before, after = folder / 'before.xlsx', folder / 'after.xlsx'
        placeholder(ROOT / 'tests/ExcelRenderer.Tests/SampleInputs' / (sample + '.xlsx'), before, address, crossing)
        data = folder / 'data.json'
        data.write_text(json.dumps({'value': value}, ensure_ascii=False))
        run(['dotnet', CLI, 'xlsx', before, '--data', data, '-o', after], log)
        cases.append((name, before, after, 'derived stress fixture; initial fixture settings are preserved during mapping/render'))
    folder = output / 'pivot-opaque'
    folder.mkdir()
    origin = ROOT / 'tests/ExcelRenderer.Mapping.Tests/bin/Release/net10.0/SampleOutputs/PivotOpaque'
    before, after = folder / 'before.xlsx', folder / 'after.xlsx'
    shutil.copyfile(origin / 'template.xlsx', before)
    shutil.copyfile(origin / 'mapped-recalculate.xlsx', after)
    cases.append(('pivot-opaque', before, after, 'synthetic pivot plus printer settings; two summaries share one updated cache'))
    for name, before, after, kind in cases:
        record = {'id': name, 'kind': kind, 'versions': {}}
        for version, source in [('before', before), ('after', after)]:
            metadata = workbook(source)
            item = {'input_sha256': sha(source), 'workbook': metadata, 'engines': {}}
            paths = []
            for engine in ['main', 'core', 'libreoffice']:
                directory = source.parent / version / engine
                directory.mkdir(parents=True)
                pdf = directory / (source.stem + '.pdf')
                if engine == 'libreoffice':
                    run([args.soffice, '-env:UserInstallation=' + (output / 'lo-profile').as_uri(), '--headless', '--convert-to', 'pdf:calc_pdf_Export', '--outdir', directory, source], log)
                else:
                    command = ['dotnet', CLI, 'render' if engine == 'main' else 'core', source, '-o', pdf, '--font-file', FONT]
                    if engine == 'main':
                        command += ['--format', 'pdf', '--no-system-fonts', '--fallback-font', 'Noto Sans JP']
                    try:
                        run(command, log)
                    except RuntimeError:
                        if name != 'pivot-opaque' or 'PivotSavedValuesMissing' not in log[-1]['stderr'] or pdf.exists():
                            raise
                        item['engines'][engine] = {'status': 'rejected-missing-pivot-saved-values', 'page_count': 0, 'pages': [], 'diagnostic': log[-1]['stderr']}
                        paths.append(None)
                        continue
                item['engines'][engine] = {'status': 'generated', **inspect(pdf)}
                paths.append(pdf)
            item['comparisons'] = {left + '-' + right: compare(item['engines'][left], item['engines'][right]) for left, right in [('main', 'core'), ('libreoffice', 'main'), ('libreoffice', 'core')]}
            contact(paths, source.parent / (version + '-contact.png'))
            record['versions'][version] = item
            print(name, version, {e: i['page_count'] for e, i in item['engines'].items()}, flush=True)
        report['cases'].append(record)
        (output / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
        (output / 'execution.json').write_text(json.dumps(log, ensure_ascii=False, indent=2) + '\n')
    # Fixed-cell Mapping must preserve original print metadata exactly.
    for case in report['cases']:
        if case['id'] in ('wrapped', 'crossing-images', 'pivot-opaque'):
            assert case['versions']['before']['workbook']['worksheet_metadata'][0]['print_settings'] == case['versions']['after']['workbook']['worksheet_metadata'][0]['print_settings']


if __name__ == '__main__':
    main()

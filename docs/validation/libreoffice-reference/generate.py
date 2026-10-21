#!/usr/bin/env python3
"""Explicitly regenerate provisional LibreOffice PDF references; never runs in CI."""
from datetime import datetime
from zoneinfo import ZoneInfo
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
import zipfile

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def run(args, **kwargs):
    return subprocess.run(args, check=True, text=True, capture_output=True, **kwargs).stdout.strip()

def main():
    import fitz
    cases = [(p.stem, p) for p in sorted((ROOT / 'tests/ExcelRenderer.Tests/SampleInputs').glob('*.xlsx'))]
    cases += [(name, ROOT / path) for name, path in [
        ('cell-argb', 'tests/ExcelRenderer.Tests/SampleInputs/CellArgb/10-cell-argb.xlsx'),
        ('border-conflicts', 'tests/ExcelRenderer.Tests/SampleInputs/BorderResolution/border-conflicts.xlsx'),
        ('richtext', 'tests/ExcelRenderer.Tests/SampleInputs/ReaderRichText/representative.xlsx'),
        ('cf-phase1', 'docs/validation/conditional-formatting/representative.xlsx'),
        ('cf-phase2', 'docs/validation/conditional-formatting-phase2/representative.xlsx'),
        ('cf-mapped-scale', 'docs/validation/conditional-formatting-phase2/mapped-scale.xlsx'),
        ('cf-phase3-icons', 'docs/validation/conditional-formatting-phase3/fixtures/icons.xlsx'),
        ('cf-phase3-layout-mapping', 'docs/validation/conditional-formatting-phase3/fixtures/layout-mapping.xlsx'),
        ('cf-phase3-legacy-bars', 'docs/validation/conditional-formatting-phase3/fixtures/legacy-bars.xlsx'),
        ('cf-phase3-x14-bars', 'docs/validation/conditional-formatting-phase3/fixtures/x14-bars.xlsx'),
        ('shapes', 'docs/validation/render-finalization-performance/inputs/shapes.xlsx'),
        ('actual-shape-text', 'docs/validation/render-finalization-performance/inputs/actual-shape-text.xlsx'),
        ('japanese-grid-report', 'samples/excel/japanese-grid-report.xlsx'),
    ]]
    pdfdir = OUT / 'pdf'
    pdfdir.mkdir(exist_ok=True)
    ns = {'s': 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'}
    records = []
    fontpaths = [ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf', ROOT / 'third_party/IPAmjMincho/ipamjm.ttf', ROOT / 'tests/ExcelRenderer.Tests/Fonts/NotoSansMono-Regular.ttf']
    with tempfile.TemporaryDirectory(prefix='lo-reference-') as temp:
        temp = Path(temp)
        conf = temp / 'fonts.conf'
        conf.write_text('<?xml version="1.0"?><!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd"><fontconfig>' + ''.join('<dir>' + escape(str(p.parent)) + '</dir>' for p in fontpaths) + '<cachedir>' + str(temp / 'cache') + '</cachedir><alias><family>sans-serif</family><prefer><family>Noto Sans JP</family></prefer></alias></fontconfig>')
        env = dict(os.environ, FONTCONFIG_FILE=str(conf), LANG='C.UTF-8', LC_ALL='C.UTF-8', TZ='UTC')
        run(['fc-cache', '-f'], env=env)
        fonts = run(['fc-list', '--format', '%{file}\n'], env=env).splitlines()
        version = run(['soffice', '--version'], env=env)
        for name, source in cases:
            before = sha(source)
            with zipfile.ZipFile(source) as z:
                workbook = ET.fromstring(z.read('xl/workbook.xml'))
                sheets = [dict(s.attrib) for s in workbook.findall('s:sheets/s:sheet', ns)]
                styles = ET.fromstring(z.read('xl/styles.xml'))
                requested_fonts = sorted({n.get('val') for n in styles.findall('.//s:font/s:name', ns)})
            case = temp / name
            case.mkdir()
            copied = case / (name + '.xlsx')
            shutil.copyfile(source, copied)
            command = ['soffice', '-env:UserInstallation=' + (case / 'profile').as_uri(), '--headless', '--convert-to', 'pdf:calc_pdf_Export', '--outdir', str(case), str(copied)]
            result = subprocess.run(command, env=env, capture_output=True, text=True, timeout=120)
            generated = case / (name + '.pdf')
            if result.returncode or not generated.is_file():
                raise RuntimeError(f'{name}: {result.returncode}: {result.stdout} {result.stderr}')
            assert sha(source) == before, source
            destination = pdfdir / generated.name
            shutil.copyfile(generated, destination)
            with fitz.open(destination) as doc:
                pages = [{'page': i+1, 'width_pt': p.rect.width, 'height_pt': p.rect.height, 'text_characters': len(p.get_text()), 'drawing_count': len(p.get_drawings())} for i,p in enumerate(doc)]
                assert pages
                for page in doc:
                    page.get_pixmap(dpi=100)  # Every page must rasterize.
            record = {'id': name, 'input': source.relative_to(ROOT).as_posix(), 'input_sha256': before, 'pdf': destination.relative_to(OUT).as_posix(), 'pdf_sha256': sha(destination), 'sheets': sheets, 'requested_fonts': requested_fonts, 'pages': pages, 'stdout': result.stdout, 'stderr': result.stderr}
            records.append(record)
            print(f'{name}: {len(pages)} page(s)', flush=True)
        manifest = {'reference_kind': 'libreoffice-provisional-not-excel', 'source_commit': run(['git', 'rev-parse', 'HEAD'], cwd=ROOT), 'generated_date_jst': datetime.now(ZoneInfo('Asia/Tokyo')).date().isoformat(), 'libreoffice': version, 'platform': platform.platform(), 'locale': 'C.UTF-8', 'timezone': 'UTC', 'export_filter': 'calc_pdf_Export', 'export_policy': 'whole workbook; original print settings; no fit-to-one-page override; no input saves', 'fontconfig': 'isolated Noto Sans JP / IPAmjMincho / Noto Sans Mono directories', 'available_fonts': [{'file': Path(f).relative_to(ROOT).as_posix(), 'sha256': sha(Path(f))} for f in sorted(set(fonts))], 'cases': records}
        (OUT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')

if __name__ == '__main__':
    main()

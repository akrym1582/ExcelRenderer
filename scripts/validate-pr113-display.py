#!/usr/bin/env python3
"""Audit PR 113 display differences without changing source workbooks or historical references."""
import argparse
import json
import os
from pathlib import Path
import runpy
import shutil
import subprocess
import sys

import fitz

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
HELPERS = runpy.run_path(str(ROOT / 'scripts/validate-pr113-forms.py'))
CLI = ROOT / 'src/ExcelRenderer.Tool/bin/Release/net10.0/ExcelRenderer.Tool.dll'
FONT = ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'


def product_command(engine, source, pdf):
    command = ['dotnet', CLI, 'render' if engine == 'main' else 'core', source,
               '-o', pdf, '--font-file', FONT]
    if engine == 'main':
        command += ['--format', 'pdf', '--no-system-fonts', '--fallback-font', 'Noto Sans JP']
    return command


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--carlito', type=Path, required=True)
    parser.add_argument('--soffice', type=Path, required=True)
    parser.add_argument('--run', type=Path, required=True)
    args = parser.parse_args()
    output = args.run.resolve()
    output.mkdir(parents=True, exist_ok=False)
    (output / 'fonts').mkdir()
    reference_font = output / 'fonts/Carlito-Regular.ttf'
    shutil.copyfile(args.carlito, reference_font)
    config = output / 'fonts.conf'
    from xml.sax.saxutils import escape
    config.write_text(f'''<?xml version="1.0"?>
<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
<fontconfig>
  <dir>{escape(str(ROOT / 'third_party/NotoSansJP'))}</dir>
  <dir>{escape(str(output / 'fonts'))}</dir>
  <cachedir>{escape(str(output / 'font-cache'))}</cachedir>
  <alias><family>Calibri</family><prefer><family>Carlito</family></prefer></alias>
  <alias><family>sans-serif</family><prefer><family>Noto Sans JP</family></prefer></alias>
</fontconfig>
''')
    os.environ['FONTCONFIG_FILE'] = str(config)
    lo = [args.soffice.resolve(), '-env:UserInstallation=' + (output / 'lo-profile').as_uri(), '--headless']
    log = [{'reference_font_sha256': HELPERS['sha'](reference_font),
            'fontconfig_calibri': subprocess.check_output(['fc-match', 'Calibri'], text=True)}]
    invoice = ROOT / 'docs/validation/pr113-review/forms/invoice-repeat/after.xlsx'
    pivot = ROOT / 'docs/validation/pr113-review/forms/pivot-opaque/after.xlsx'
    originals = {str(p): HELPERS['sha'](p) for p in (invoice, pivot)}

    def render_three(source, folder):
        paths, report = [], {}
        for engine in ('main', 'core', 'libreoffice'):
            destination = folder / engine
            destination.mkdir(parents=True)
            pdf = destination / (source.stem + '.pdf')
            command = (lo + ['--convert-to', 'pdf:calc_pdf_Export', '--outdir', destination, source]
                       if engine == 'libreoffice' else product_command(engine, source, pdf))
            HELPERS['run'](command, log)
            report[engine] = HELPERS['inspect'](pdf)
            paths.append(pdf)
        HELPERS['contact'](paths, folder / 'contact.png')
        (folder / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
        comparisons = {engine + '-libreoffice': HELPERS['compare'](report[engine], report['libreoffice'])
                       for engine in ('main', 'core')}
        (folder / 'comparisons.json').write_text(json.dumps(comparisons, ensure_ascii=False, indent=2) + '\n')
        return paths, report

    paths, _ = render_three(invoice, output / 'invoice')
    for path in paths[:2]:
        with fitz.open(path) as document:
            red = [''.join(chr(c[0]) for c in span['chars']) for page in document
                   for span in page.get_texttrace() if span['color'] == (1, 0, 0)]
            assert len(red) == 3 and any('500' in t for t in red) and any('1,234.50' in t for t in red), red

    for engine in ('main', 'core'):
        folder = output / 'pivot-missing' / engine
        folder.mkdir(parents=True)
        pdf = folder / 'after.pdf'
        command = list(map(str, product_command(engine, pivot, pdf)))
        result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
        log.append({'command': command, 'exit_code': result.returncode, 'stdout': result.stdout,
                    'stderr': result.stderr, 'pdf_exists': pdf.exists()})
        assert result.returncode != 0 and not pdf.exists() and 'PivotSavedValuesMissing' in result.stderr, log[-1]

    # A reference-only copy: LibreOffice can discard opaque parts and re-evaluate
    # filters on re-open. This must never replace the Mapping output.
    folder = output / 'pivot-refreshed-reference'
    folder.mkdir()
    HELPERS['run'](lo + ['--convert-to', 'xlsx:Calc MS Excel 2007 XML', '--outdir', folder, pivot], log)
    _, report = render_three(folder / 'after.xlsx', folder)
    for result in report.values():
        assert result['page_count'] == 1
        assert ''.join(page['text'] for page in result['pages']).count('Total Resu') == 4
    assert originals == {str(p): HELPERS['sha'](p) for p in (invoice, pivot)}
    (output / 'execution.json').write_text(json.dumps(log, ensure_ascii=False, indent=2) + '\n')
    (output / 'source-hashes.json').write_text(json.dumps(originals, indent=2) + '\n')
    print('Named colors, missing-pivot rejection and saved-pivot display verified; reference differences retained.')


if __name__ == '__main__':
    main()

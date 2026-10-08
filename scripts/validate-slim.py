#!/usr/bin/env python3
"""Run Slim/reference comparisons in separate processes and record measurements."""
import argparse
import hashlib
import json
import pathlib
import platform
import statistics
import subprocess
from PIL import Image, ImageChops, ImageStat

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / 'TestResults/Slim/Validation'
FONT = ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
SLIM = ROOT / 'tools/ExcelRenderer.Slim.Validation/bin/Release/net10.0/ExcelRenderer.Slim.Validation.dll'
REFERENCE = ROOT / 'tools/ExcelRenderer.Slim.Reference/bin/Release/net10.0/ExcelRenderer.Slim.Reference.dll'
DPI = 96


def run(*args):
    return subprocess.check_output([str(a) for a in args], cwd=ROOT, text=True, stderr=subprocess.STDOUT)


def render(executable, source, output, repeats=1):
    return json.loads(run('dotnet', executable, 'render', source, output, FONT, repeats).strip().splitlines()[-1])


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--visual-only', action='store_true')
    parser.add_argument('--accept-baseline', action='store_true')
    parser.add_argument('--reuse-reference', action='store_true')
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    previous = json.loads((OUT / 'report.json').read_text()) if args.reuse_reference and (OUT / 'report.json').exists() else {}
    for name in ['Validation', 'Reference']:
        project = ROOT / f'tools/ExcelRenderer.Slim.{name}/ExcelRenderer.Slim.{name}.csproj'
        run('dotnet', 'restore', project, '--locked-mode')
        run('dotnet', 'build', project, '-c', 'Release', '--no-restore')
    report = {
        'os': platform.platform(), 'dotnet': run('dotnet', '--version').strip(),
        'renderer': run('pdftoppm', '-v').strip(), 'dpi': DPI,
        'font_sha256': hashlib.sha256(FONT.read_bytes()).hexdigest(),
        'source_commit': '32e9165d145516dbd0bcb4c2ae31a78ce6774bce',
        'visual': [], 'performance': [],
    }
    baselines = ROOT / 'tests/ExcelRenderer.Slim.Tests/Baselines'
    if args.accept_baseline:
        baselines.mkdir(parents=True, exist_ok=True)
    for source in sorted((ROOT / 'tests/ExcelRenderer.Tests/SampleInputs').glob('*.xlsx')):
        normalized = OUT / source.name
        run('dotnet', SLIM, 'normalize', source, normalized)
        full_pdf, slim_pdf = OUT / (source.stem + '-reference.pdf'), OUT / (source.stem + '-slim.pdf')
        full, slim = render(REFERENCE, normalized, full_pdf), render(SLIM, normalized, slim_pdf)
        if full[0]['Pages'] != slim[0]['Pages']:
            raise AssertionError(f'Page counts differ: {source.name}')
        for kind, pdf in [('reference', full_pdf), ('slim', slim_pdf)]:
            for old in OUT.glob(source.stem + '-' + kind + '-*.png'):
                old.unlink()
            run('pdftoppm', '-r', DPI, '-png', pdf, OUT / (source.stem + '-' + kind))
        for actual in sorted(OUT.glob(source.stem + '-slim-*.png')):
            reference = actual.with_name(actual.name.replace('-slim-', '-reference-'))
            with Image.open(actual) as a, Image.open(reference) as b:
                difference = ImageChops.difference(a.convert('RGB'), b.convert('RGB'))
                mae = sum(ImageStat.Stat(difference).mean) / 3
                channels = difference.split()
                mask = ImageChops.lighter(ImageChops.lighter(channels[0], channels[1]), channels[2])
                changed = 1 - mask.histogram()[0] / (a.width * a.height)
                difference.save(OUT / actual.name.replace('-slim-', '-difference-'))
            item = {'page': actual.name, 'mae_0_255': mae, 'changed_fraction': changed}
            baseline = baselines / actual.name
            if args.accept_baseline:
                baseline.write_bytes(actual.read_bytes())
            elif not baseline.exists():
                raise AssertionError(f'Golden raster is missing: {actual.name}')
            else:
                with Image.open(actual) as a, Image.open(baseline) as b:
                    delta = ImageChops.difference(a.convert('RGB'), b.convert('RGB'))
                    item['baseline_mae'] = sum(ImageStat.Stat(delta).mean) / 3
                    # Rasterizer versions can differ in antialiasing; a mean channel delta
                    # above 0.25 requires review. Numeric PDF tests catch small shifts.
                    if item['baseline_mae'] > 0.25:
                        raise AssertionError(f'Golden raster changed: {actual.name}')
            if mae > 0.25:
                raise AssertionError(f'Reference raster differs: {actual.name}: MAE={mae}')
            report['visual'].append(item)
        # Text extraction provides a second check that Japanese survives encoding.
        extracted = run('pdftotext', slim_pdf, '-')
        if source.name.startswith('01-') and not any('\u3000' <= c <= '\u9fff' for c in extracted):
            raise AssertionError('Japanese text extraction is empty')
    if not args.visual_only:
        for mode in ['text', 'images']:
            for rows in [1000, 5000, 10000]:
                source = OUT / f'benchmark-{mode}-{rows}.xlsx'
                run('dotnet', SLIM, 'generate', source, rows, mode)
                saved = next((case for case in previous.get('performance', []) if case['mode'] == mode and case['rows'] == rows), None)
                samples = {'slim': [], 'reference': saved['samples']['reference'] if saved else []}
                # Alternate isolated processes, rather than run competitors simultaneously.
                for trial in range(3):
                    for kind, executable in [('reference', REFERENCE), ('slim', SLIM)]:
                        if kind == 'reference' and saved:
                            continue
                        samples[kind].extend(render(executable, source, OUT / f'{mode}-{rows}-{kind}-{trial}.pdf'))
                medians = {kind: {metric: statistics.median(s[metric] for s in values)
                                  for metric in ['Milliseconds', 'PeakRss', 'ManagedMax', 'Pages', 'PdfBytes']}
                           for kind, values in samples.items()}
                report['performance'].append({'mode': mode, 'rows': rows, 'samples': samples, 'medians': medians,
                    'time_ratio': medians['slim']['Milliseconds'] / medians['reference']['Milliseconds'],
                    'rss_ratio': medians['slim']['PeakRss'] / medians['reference']['PeakRss']})
                (OUT / 'report.json').write_text(json.dumps(report, indent=2))
        source = OUT / 'repeat-text-100.xlsx'
        run('dotnet', SLIM, 'generate', source, 100, 'text')
        report['repeat_fixture_rows'] = 100
        report['repeat50'] = render(SLIM, source, OUT / 'repeat.pdf', 50)
    (OUT / 'report.json').write_text(json.dumps(report, indent=2))
    if args.accept_baseline:
        (baselines / 'provenance.json').write_text(json.dumps({k: report[k] for k in ['source_commit', 'os', 'dotnet', 'font_sha256', 'renderer', 'dpi']}, indent=2))
    print(f"{len(report['visual'])} raster pages checked; {len(report['performance'])} benchmark cases. {OUT / 'report.json'}")


if __name__ == '__main__':
    main()

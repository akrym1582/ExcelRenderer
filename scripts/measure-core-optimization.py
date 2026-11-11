#!/usr/bin/env python3
"""Measure saved Release runtimes sequentially, reusing identical fixture bytes."""
import argparse
import hashlib
import json
import os
import pathlib
import platform
import statistics
import subprocess
import xml.etree.ElementTree as E
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--rounds', type=int, default=3)
parser.add_argument('--prepare-only', action='store_true')
args = parser.parse_args()
OUTPUT = ROOT / 'docs/validation/core-performance-optimization'
RUNTIMES = ROOT / 'artifacts/core-performance'
FIXTURES = OUTPUT / 'inputs'
FIXTURES.mkdir(parents=True, exist_ok=True)
FONT = ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
PHASES = ['baseline', 'phase1', 'phase2', 'phase3', 'phase4', 'phase5']
NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
E.register_namespace('', NS)

def run(*command):
    return subprocess.check_output([str(item) for item in command], cwd=ROOT, text=True, stderr=subprocess.STDOUT, env=dict(os.environ, CORE_PERF_DIAGNOSTICS="0"))

def executable(phase):
    return RUNTIMES / phase / 'ExcelRenderer.Core.Validation.dll'

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def generate(name, rows, kind=None):
    path = FIXTURES / (name + '.xlsx')
    if path.exists():
        return path
    command = ['dotnet', executable('baseline'), 'generate', path, rows]
    if kind in {'images', 'imagesonly'}:
        command.append('images')
    run(*command)
    if kind not in {'wrap', 'borders', 'repeat', 'mixed', 'empty', 'header', 'imagesonly'}:
        return path
    with zipfile.ZipFile(path) as archive:
        files = {name: archive.read(name) for name in archive.namelist()}
    styles = E.fromstring(files['xl/styles.xml'])
    formats = styles.find(f'{{{NS}}}cellXfs')
    fmt = formats[0]
    if kind == 'wrap':
        E.SubElement(fmt, f'{{{NS}}}alignment', {'wrapText': '1'})
    if kind == 'borders':
        borders = styles.find(f'{{{NS}}}borders')
        border = E.SubElement(borders, f'{{{NS}}}border')
        for side in ['left', 'right', 'top', 'bottom']:
            node = E.SubElement(border, f'{{{NS}}}{side}', {'style': 'thin'})
            E.SubElement(node, f'{{{NS}}}color', {'rgb': 'FF336699'})
        borders.set('count', str(len(borders)))
        fmt.set('borderId', '1')
        fills = styles.find(f'{{{NS}}}fills')
        fill = E.SubElement(fills, f'{{{NS}}}fill')
        pattern = E.SubElement(fill, f'{{{NS}}}patternFill', {'patternType': 'solid'})
        E.SubElement(pattern, f'{{{NS}}}fgColor', {'rgb': 'FFDDEEFF'})
        fills.set('count', str(len(fills)))
        fmt.set('fillId', str(len(fills)-1))
    files['xl/styles.xml'] = E.tostring(styles)
    sheet = E.fromstring(files['xl/worksheets/sheet1.xml'])
    if kind in {'empty', 'header', 'imagesonly'}:
        sheet.find(f'{{{NS}}}sheetData').clear()
    if kind == 'header':
        header = E.SubElement(sheet, f'{{{NS}}}headerFooter')
        E.SubElement(header, f'{{{NS}}}oddHeader').text = '&CCore &P / &N'
    for index, text in enumerate(sheet.iter(f'{{{NS}}}t')):
        if kind == 'wrap':
            text.text = str(index) + ' 日本語とASCII abc 結合e\u0301 😀  ' * 12
        elif kind == 'repeat':
            text.text = '反復見出し ' + str(index % 12)
        elif kind == 'mixed':
            text.text = '見出し' if index % 3 else str(index)
    files['xl/worksheets/sheet1.xml'] = E.tostring(sheet)
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    return path

cases = [generate('text'+str(rows), rows) for rows in [1, 1000, 5000, 10000]]
cases += [generate(kind, 1, kind) for kind in ['empty', 'header', 'imagesonly']]
cases += [generate(kind+'1000', 1000, kind) for kind in ['borders', 'wrap', 'repeat', 'mixed', 'images']]
metadata = {
    'baseline_commit': (OUTPUT/'baseline-commit.txt').read_text().strip(),
    'phase_patches': {path.name: sha(path) for path in OUTPUT.glob('*.patch')},
    'protocol': 'Sequential isolated processes; alternating forward/reverse phase order by round; iteration 0 cold, then three warm conversions; no overlapping builds/tests/renders. Timings are inclusive and are not summed.',
    'sdk': run('dotnet', '--info'), 'os': platform.platform(),
    'cpu': pathlib.Path('/proc/cpuinfo').read_text().split('model name')[1].split('\n')[0].strip(' :'),
    'processor_count': os.cpu_count(), 'font_sha256': sha(FONT),
    'inputs': {str(path.relative_to(ROOT)): sha(path) for path in cases},
    'runtimes': {phase: {p.name: sha(p) for p in (RUNTIMES/phase).glob('*.dll')} for phase in PHASES},
    'observer_enabled': False, 'warmup_conversions_per_process': 1,
    'inherited_runtime_settings': {name: os.environ.get(name, 'runtime default (not set)') for name in
        ['DOTNET_TieredCompilation', 'COMPlus_TieredCompilation', 'DOTNET_GCConcurrent', 'COMPlus_gcConcurrent',
         'DOTNET_GCServer', 'COMPlus_gcServer', 'DOTNET_ReadyToRun', 'DOTNET_PROCESSOR_COUNT']},
    'cpu_quota': pathlib.Path('/sys/fs/cgroup/cpu.max').read_text().strip(),
    'affinity': sorted(os.sched_getaffinity(0)),
}
(OUTPUT/'environment.json').write_text(json.dumps(metadata, indent=2)+'\n')
if args.prepare_only:
    raise SystemExit(0)
raw = OUTPUT/'samples.jsonl'
raw.write_text('')
records = []
for round_index in range(args.rounds):
    for path in cases:
        phases = PHASES if round_index % 2 == 0 else list(reversed(PHASES))
        for phase in phases:
            pdf = RUNTIMES/phase/(path.stem+'.pdf')
            command = ['dotnet', executable(phase), 'render', path, pdf, FONT, 4]
            samples = json.loads(run(*command).splitlines()[-1])
            record = {'round': round_index, 'case': path.stem, 'phase': phase, 'command': list(map(str, command)), 'samples': samples}
            records.append(record)
            with raw.open('a') as stream:
                stream.write(json.dumps(record)+'\n')
            print(round_index, path.stem, phase, round(statistics.median(s['Milliseconds'] for s in samples[1:]), 2), flush=True)
summary = []
for path in cases:
    for phase in PHASES:
        rounds = []
        for record in records:
            if record['case'] == path.stem and record['phase'] == phase:
                rounds.append({'round': record['round'], 'cold': record['samples'][0], 'warm_medians': {
                    key: statistics.median(s[key] for s in record['samples'][1:])
                    for key in ['Milliseconds', 'AllocatedBytes', 'PeakRss', 'PdfBytes', 'Pages']}})
        summary.append({'case': path.stem, 'phase': phase, 'rounds': rounds})
(OUTPUT/'summary.json').write_text(json.dumps(summary, indent=2)+'\n')

# A separate diagnostics run keeps observation overhead out of primary timings.
diagnostics = []
for path in cases:
    for phase in PHASES:
        command = ['dotnet', executable(phase), 'render', path, RUNTIMES/phase/(path.stem+'-diagnostic.pdf'), FONT, 1]
        environment = dict(os.environ, CORE_PERF_DIAGNOSTICS='1')
        value = subprocess.check_output(list(map(str, command)), cwd=ROOT, text=True, stderr=subprocess.STDOUT, env=environment)
        sample = json.loads(value.splitlines()[-1])[0]
        diagnostics.append({'case': path.stem, 'phase': phase, 'sample': sample})
(OUTPUT/'diagnostics.json').write_text(json.dumps(diagnostics, indent=2)+'\n')

main_records = []
main_cases = ['02-image', '04-text-decoration', '05-borders', '06-layout-and-pagination']
for round_index in range(args.rounds):
    for name in main_cases:
        path = ROOT/'tests/ExcelRenderer.Tests/SampleInputs'/(name+'.xlsx')
        for fmt in ['Pdf', 'Png', 'Svg']:
            phases = ['baseline-main', 'final-main'] if round_index % 2 == 0 else ['final-main', 'baseline-main']
            for phase in phases:
                tool = RUNTIMES/phase/'ExcelRenderer.Performance.dll'
                command = ['dotnet', tool, 'render-case', path, fmt, 'Paginated', 4]
                samples = [json.loads(line) for line in run(*command).splitlines() if line.startswith('{')]
                samples = [sample for sample in samples if 'TotalMs' in sample]
                assert len(samples) == 4, (phase, name, fmt)
                main_records.append({'round': round_index, 'case': name, 'format': fmt, 'phase': phase,
                                     'command': list(map(str, command)), 'samples': samples})
                print('main', round_index, name, fmt, phase, flush=True)
(OUTPUT/'main-samples.json').write_text(json.dumps(main_records, indent=2)+'\n')
metadata['main_runtimes'] = {phase: {p.name: sha(p) for p in (RUNTIMES/phase).glob('*.dll')}
                             for phase in ['baseline-main', 'final-main']}
metadata['main_inputs'] = {name: sha(ROOT/'tests/ExcelRenderer.Tests/SampleInputs'/(name+'.xlsx')) for name in main_cases}
metadata['main_protocol'] = 'Existing render-case observer enabled equally in both runtimes; iteration 0 cold, three warm conversions; harness GC outside conversion between trials; compare within this protocol only.'
(OUTPUT/'environment.json').write_text(json.dumps(metadata, indent=2)+'\n')

#!/usr/bin/env python3
"""Measure integration stages in fresh Release processes, with one warmup and five samples."""
import argparse
import hashlib
import platform
import json
import pathlib
import statistics
import subprocess
import time

parser = argparse.ArgumentParser()
parser.add_argument('--fixtures', required=True, type=pathlib.Path)
parser.add_argument('--output', required=True, type=pathlib.Path)
parser.add_argument('--resume', action='store_true', help='Keep completed backend cases from the output JSON.')
parser.add_argument('--quick', action='store_true', help='Only the 10,000-row plain mapping and reader cases.')
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
program = root / 'tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll'
result = {'warmup': 1, 'samples': 5, 'configuration': 'Release', 'culture': 'en-US', 'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip(), 'cases': []}
result['environment'] = platform.platform()
result['fontSha256'] = hashlib.sha256((root / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf').read_bytes()).hexdigest()
if args.resume and args.output.exists():
    previous = json.loads(args.output.read_text())
    assert previous['sourceCommit'] == result['sourceCommit']
    result = previous
cases = []
for rows in ([10000] if args.quick else [10000, 100000]):
    for kind in (['plain'] if args.quick else ['plain', 'nested', 'geometry']):
        cases.append(('mapping', kind, rows, 'template', ['closedxml', 'openxml', 'auto']))
    cases.append(('reader', 'plain', rows, 'values', ['closedxml', 'openxml', 'auto']))
    if not args.quick:
        cases.append(('render', 'plain', rows, 'values', ['closedxml', 'openxml']))
        cases.append(('mapping-render', 'plain', rows, 'template', ['closedxml', 'openxml']))
args.output.parent.mkdir(parents=True, exist_ok=True)
started = time.monotonic()
for operation, kind, rows, suffix, backends in cases:
    source = args.fixtures.resolve() / str(rows) / f'{kind}-{rows}-{suffix}.xlsx'
    for backend in backends:
        if any((case['operation'], case['kind'], case['rows'], case['backend']) == (operation, kind, rows, backend) for case in result['cases']):
            continue
        samples = []
        for index in range(6):
            process = subprocess.run(['dotnet', str(program), 'integration-worker', str(source), operation, backend, str(rows)], cwd=root, capture_output=True, text=True, check=True)
            sample = json.loads(process.stdout.strip().splitlines()[-1])
            sample['SpoolBytes'] = sample['IoMetrics'].get('spool.fileBytes', 0)
            sample['ClosedXmlWorkbooks'] = sample['IoMetrics'].get('closedXml.workbooks', 0) + sample['Metrics'].get('coreReaderWorkbook', 0)
            sample['warmup'] = index == 0
            samples.append(sample)
            print(f'{operation} {kind} {rows} {backend}: {index}/5 {sample["ElapsedMs"]:.1f} ms', flush=True)
        measured = samples[1:]
        summary = {key: statistics.median(sample[key] for sample in measured) for key in ['ElapsedMs', 'AllocatedBytes', 'PeakRssBytes', 'Cells', 'MappingOutputBytes', 'SpoolBytes', 'ClosedXmlWorkbooks']}
        result['cases'].append({'operation': operation, 'kind': kind, 'rows': rows, 'backend': backend, 'input': str(source), 'inputSha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'median': summary, 'raw': samples})
        result['elapsedSeconds'] = time.monotonic() - started
        args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')

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
import zipfile
from xml.dom import minidom

parser = argparse.ArgumentParser()
parser.add_argument('--fixtures', required=True, type=pathlib.Path)
parser.add_argument('--output', required=True, type=pathlib.Path)
parser.add_argument('--resume', action='store_true', help='Keep completed cases from the output JSON.')
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
# The generator assigns Noto to the worksheet, which serializes a column style
# across all 16,384 columns. Limit that font-only default to the actual fixture
# columns, retaining every used value, blank fill, dimension and print setting.
fixture_root = args.fixtures / 'bounded'
for count in ([10000] if args.quick else [10000, 100000]):
    (fixture_root / str(count)).mkdir(parents=True, exist_ok=True)
    for kind in ['plain', 'nested', 'geometry']:
        for suffix in ['template', 'values']:
            name = f'{kind}-{count}-{suffix}.xlsx'
            with zipfile.ZipFile(args.fixtures / str(count) / name) as source, zipfile.ZipFile(fixture_root / str(count) / name, 'w') as target:
                for entry in source.infolist():
                    content = source.read(entry.filename)
                    if entry.filename == 'xl/worksheets/sheet1.xml':
                        content = content.replace(b'max="16384"', b'max="4"' if kind == 'geometry' else b'max="2"')
                    target.writestr(entry, content)
# Keep the large print-layout comparison linear for the compatibility executor:
# a merged header, repeated detail values/styled blanks, and an expanding print area.
# Repeated per-detail merges are covered separately by functional and supplemental measurements.
if not args.quick:
    for count in [10000, 100000]:
        original = fixture_root / str(count) / f'geometry-{count}-template.xlsx'
        variant = fixture_root / str(count) / f'geometry-header-{count}-template.xlsx'
        with zipfile.ZipFile(original) as source, zipfile.ZipFile(variant, 'w') as target:
            for entry in source.infolist():
                content = source.read(entry.filename)
                if entry.filename == 'xl/worksheets/sheet1.xml':
                    document = minidom.parseString(content)
                    namespace = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
                    rows = document.getElementsByTagNameNS(namespace, 'row')
                    first = next(row for row in rows if row.getAttribute('r') == '1')
                    cell = next(cell for cell in document.getElementsByTagNameNS(namespace, 'c') if cell.getAttribute('r') == 'C2')
                    cell.setAttribute('r', 'C1')
                    first.appendChild(cell)
                    merge = document.getElementsByTagNameNS(namespace, 'mergeCell')[0]
                    assert merge.getAttribute('ref') == 'C2:D2'
                    merge.setAttribute('ref', 'C1:D1')
                    content = document.toxml(encoding='utf-8')
                target.writestr(entry, content)
result["runnerSha256"] = hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest()
prior_elapsed = result.get("elapsedSeconds", 0)
cases = []
for rows in ([10000] if args.quick else [10000, 100000]):
    for kind in (['plain'] if args.quick else ['plain', 'nested', 'geometry-header']):
        cases.append(('mapping', kind, rows, 'template'))
    cases.append(('reader', 'plain', rows, 'values'))
    if not args.quick:
        cases.append(('render', 'plain', rows, 'values'))
        cases.append(('mapping-render', 'plain', rows, 'template'))
args.output.parent.mkdir(parents=True, exist_ok=True)
started = time.monotonic()
for operation, kind, rows, suffix in cases:
    source = fixture_root.resolve() / str(rows) / f'{kind}-{rows}-{suffix}.xlsx'
    if any((case['operation'], case['kind'], case['rows']) == (operation, kind, rows) for case in result['cases']):
        continue
    samples = []
    for index in range(6):
        process = subprocess.run(['dotnet', str(program), 'integration-worker', str(source), operation, str(rows)], cwd=root, capture_output=True, text=True, check=True)
        sample = json.loads(process.stdout.strip().splitlines()[-1])
        sample['SpoolBytes'] = sample['IoMetrics'].get('spool.fileBytes', 0)
        sample['ClosedXmlWorkbooks'] = sample['IoMetrics'].get('closedXml.workbooks', 0) + sample['Metrics'].get('coreReaderWorkbook', 0)
        sample['warmup'] = index == 0
        samples.append(sample)
        print(f'{operation} {kind} {rows}: {index}/5 {sample["ElapsedMs"]:.1f} ms', flush=True)
    measured = samples[1:]
    summary = {key: statistics.median(sample[key] for sample in measured) for key in ['ElapsedMs', 'AllocatedBytes', 'PeakRssBytes', 'Cells', 'MappingOutputBytes', 'SpoolBytes', 'ClosedXmlWorkbooks']}
    result['cases'].append({'operation': operation, 'kind': kind, 'rows': rows, 'input': str(source), 'inputSha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'median': summary, 'raw': samples})
    result['elapsedSeconds'] = prior_elapsed + time.monotonic() - started
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')

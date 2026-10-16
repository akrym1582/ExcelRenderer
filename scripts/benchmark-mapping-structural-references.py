#!/usr/bin/env python3
"""Compare isolated Release Mapping workers on identical structural inputs."""
import argparse
import hashlib
import json
import pathlib
import platform
import statistics
import subprocess
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--baseline', required=True, type=pathlib.Path)
parser.add_argument('--current', required=True, type=pathlib.Path)
parser.add_argument('--fixtures', required=True, type=pathlib.Path)
parser.add_argument('--output', required=True, type=pathlib.Path)
parser.add_argument('--samples', type=int, default=3)
parser.add_argument('--reuse-baseline', type=pathlib.Path, help='Reuse captured baseline samples with the same binaries and existing fixture/output files.')
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
ns = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
args.fixtures.mkdir(parents=True, exist_ok=True)
previous = json.loads(args.reuse_baseline.read_text()) if args.reuse_baseline else None
if previous is None:
    subprocess.run(['dotnet', str(args.current), 'integration-generate', str(args.fixtures), '1'], cwd=root, check=True)
plain = args.fixtures / 'plain-1-template.xlsx'
for kind in ['expression', 'population']:
    with zipfile.ZipFile(plain) as source, zipfile.ZipFile(args.fixtures / f'{kind}.xlsx', 'w', zipfile.ZIP_DEFLATED) as target:
        for entry in source.infolist():
            content = source.read(entry.filename)
            if entry.filename == 'xl/worksheets/sheet1.xml':
                sheet = ET.fromstring(content)
                rule = ET.SubElement(sheet, f'{{{ns}}}conditionalFormatting', sqref='B2 D2')
                cf = ET.SubElement(rule, f'{{{ns}}}cfRule', type='expression' if kind == 'expression' else 'colorScale', priority='1')
                if kind == 'expression':
                    ET.SubElement(cf, f'{{{ns}}}formula').text = 'B2>0'
                else:
                    scale = ET.SubElement(cf, f'{{{ns}}}colorScale')
                    for value in ['min', 'max']:
                        ET.SubElement(scale, f'{{{ns}}}cfvo', type=value)
                    for value in ['FFFF0000', 'FF00FF00']:
                        ET.SubElement(scale, f'{{{ns}}}color', rgb=value)
                content = ET.tostring(sheet, encoding='utf-8', xml_declaration=True)
            target.writestr(entry, content)

# A longer template exercises many distinct relative range queries after one insertion.
for count in [1000, 10000]:
    with zipfile.ZipFile(plain) as source, zipfile.ZipFile(args.fixtures / f'ranges-{count}.xlsx', 'w', zipfile.ZIP_DEFLATED) as target:
        for entry in source.infolist():
            content = source.read(entry.filename)
            if entry.filename == 'xl/worksheets/sheet1.xml':
                sheet = ET.fromstring(content)
                data = sheet.find(f'{{{ns}}}sheetData')
                data.remove(list(data)[-1])
                for row in range(3, count + 3):
                    record = ET.SubElement(data, f'{{{ns}}}row', r=str(row))
                    cell = ET.SubElement(record, f'{{{ns}}}c', r=f'A{row}')
                    ET.SubElement(cell, f'{{{ns}}}v').text = str(row)
                cf = ET.SubElement(sheet, f'{{{ns}}}conditionalFormatting', sqref=f'B3:B{count + 2}')
                rule = ET.SubElement(cf, f'{{{ns}}}cfRule', type='expression', priority='1')
                ET.SubElement(rule, f'{{{ns}}}formula').text = 'SUM(A3:A4)>0'
                content = ET.tostring(sheet, encoding='utf-8', xml_declaration=True)
            target.writestr(entry, content)

report = {'configuration': 'Release', 'warmups': 1, 'samples': args.samples, 'environment': platform.platform(), 'cases': []}
report['runnerSha256'] = hashlib.sha256(pathlib.Path(__file__).read_bytes()).hexdigest()
report['binaries'] = {label: {name: hashlib.sha256((binary.parent / name).read_bytes()).hexdigest()
                            for name in ['ExcelRenderer.Performance.dll', 'ExcelRenderer.Mapping.dll', 'ExcelRenderer.OpenXml.dll']}
                      for label, binary in [('baseline', args.baseline), ('current', args.current)]}
report['baselineCommit'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=args.baseline.parents[5], text=True).strip()
report['currentHead'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
if previous is not None:
    assert previous['binaries']['baseline'] == report['binaries']['baseline']
    assert previous['samples'] == args.samples
    report['baselineCaptureSha256'] = hashlib.sha256(args.reuse_baseline.read_bytes()).hexdigest()
report['currentSourceDiffSha256'] = hashlib.sha256(subprocess.check_output(['git', 'diff', 'HEAD', '--', 'src'], cwd=root)).hexdigest()
for kind, rows in [('plain', 10000), ('plain', 100000), ('expression', 1000), ('expression', 10000), ('population', 1000), ('population', 10000), ('ranges', 1000), ('ranges', 10000)]:
    path = plain if kind == 'plain' else args.fixtures / (f'ranges-{rows}.xlsx' if kind == 'ranges' else f'{kind}.xlsx')
    case = {'kind': kind, 'rows': rows, 'inputSha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'runs': {}}
    for label, binary in [('baseline', args.baseline), ('current', args.current)]:
        if label == 'baseline' and previous is not None:
            prior = next((item for item in previous['cases'] if (item['kind'], item['rows']) == (kind, rows)), None)
            if prior is not None:
                assert prior['inputSha256'] == case['inputSha256']
                case['runs'][label] = prior['runs'][label]
                print(f'{kind} {rows} baseline: reuse identical-input capture', flush=True)
                continue
        samples = []
        for iteration in range(args.samples + 1):
            output = args.fixtures / f'{kind}-{rows}-{label}.xlsx'
            worker = subprocess.run(['dotnet', str(binary), 'integration-worker', str(path), 'mapping', 'openxml', str(2 if kind == 'ranges' else rows), str(output)], cwd=root, check=True, text=True, capture_output=True)
            sample = json.loads(worker.stdout.strip().splitlines()[-1])
            sample['warmup'] = iteration == 0
            samples.append(sample)
            print(f'{kind} {rows} {label} {iteration}: {sample["ElapsedMs"]:.1f} ms', flush=True)
        case['runs'][label] = {'median': {key: statistics.median(sample[key] for sample in samples[1:]) for key in ['ElapsedMs', 'AllocatedBytes', 'PeakRssBytes', 'MappingOutputBytes']}, 'samples': samples}
    # Compare mapped cell payloads separately from the intentionally corrected rule XML.
    def cells(label):
        with zipfile.ZipFile(args.fixtures / f'{kind}-{rows}-{label}.xlsx') as package:
            sheet = ET.fromstring(package.read('xl/worksheets/sheet1.xml'))
            return [(cell.attrib, ''.join(cell.itertext())) for cell in sheet.iter(f'{{{ns}}}c')]
    assert cells('baseline') == cells('current'), (kind, rows)
    case['mappedCellsEqual'] = True
    report['cases'].append(case)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')

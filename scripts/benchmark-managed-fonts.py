#!/usr/bin/env python3
"""Compare managed subset conversion with an explicitly supplied, isolated starting-commit assembly."""
import argparse
import hashlib
import json
from pathlib import Path
import platform
import statistics
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def workbook(path, text):
    # Minimal deterministic test-only XLSX, same bytes for both measured assemblies.
    from xml.sax.saxutils import escape
    entries = {
        '[Content_Types].xml': '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>',
        '_rels/.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>',
        'xl/workbook.xml': '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Fonts" sheetId="1" r:id="r1"/></sheets></workbook>',
        'xl/_rels/workbook.xml.rels': '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="r1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="r2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>',
        'xl/styles.xml': '<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="18"/><name val="Noto Sans JP"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf/></cellStyleXfs><cellXfs count="1"><xf fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>',
        'xl/worksheets/sheet1.xml': '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><cols><col min="1" max="1" width="65" customWidth="1"/></cols><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>' + escape(text) + '</t></is></c></row></sheetData><pageSetup paperSize="9"/></worksheet>',
    }
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, data in entries.items():
            info = zipfile.ZipInfo(name, (2026, 10, 18, 0, 0, 0))
            archive.writestr(info, data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline-assembly', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    assembly = ROOT / 'tools/ExcelRenderer.Performance/bin/Release/net10.0/ExcelRenderer.Performance.dll'
    old = args.baseline_assembly.resolve()
    fixtures = {'invoice': ROOT/'samples/mapping/invoice/invoice.xlsx', 'japanese': ROOT/'samples/excel/japanese-grid-report.xlsx'}
    for name, text in [('small', 'ABC 123 日本語'), ('ivs', '辻\U000E0102辻\U000E0103葛\U000E0102 𠮷'), ('color', '日本語 辻\U000E0102 😀 ☀️')]:
        fixtures[name] = output / (name + '.xlsx')
        workbook(fixtures[name], text)
    for pages in [50, 200]:
        target = output / str(pages)
        subprocess.run(['python3', str(ROOT/'scripts/benchmark-pdf-backends.py'), '--output', str(target), '--no-build', '--generate-only', '--fixture', f'{pages}-pages'], cwd=ROOT, check=True, capture_output=True)
        fixtures[f'{pages}-pages'] = target / f'{pages}-pages.xlsx'
    conditions = {'configuration': 'Release', 'warmups': 3, 'samples': 5,
                  'os': platform.platform(), 'sdk': subprocess.check_output(['dotnet', '--version'], text=True).strip(),
                  'peakRssScope': 'fresh worker process (3 warmups + final measured conversion)',
                  'thresholdRatio': 2.0, 'order': 'alternating old/new per round',
                  'assemblies': {key: {'path': str(dll), 'mainSha256': hashlib.sha256((dll.parent/'ExcelRenderer.dll').read_bytes()).hexdigest(),
                                      'coreSha256': hashlib.sha256((dll.parent/'ExcelRenderer.Core.dll').read_bytes()).hexdigest()}
                                 for key, dll in [('old', old), ('managed', assembly)]},
                  'inputs': {name: {'sha256': hashlib.sha256(path.read_bytes()).hexdigest()} for name, path in fixtures.items()},
                  'fonts': {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest()
                            for path in sorted((ROOT/'third_party').glob('*/*tf'))}}
    (output/'conditions.json').write_text(json.dumps(conditions, indent=2)+'\n')
    raw, summary = [], []
    for name, path in fixtures.items():
        for sample in range(5):
            engines = [('old', old), ('managed', assembly)]
            if sample % 2: engines.reverse()
            for stage, dll in engines:
                command = ['dotnet', str(dll), 'render-case', str(path), 'pdf', 'paginated', '4', '--font-pack', '--measure-text', '--pdf-backend', 'skiasharp']
                result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, check=True)
                records = [json.loads(line) for line in result.stdout.splitlines() if line.startswith('{')]
                measured = [record for record in records if 'TotalMs' in record][-1]
                retention = [record for record in records if 'PostCollectionIteration' in record]
                raw.append({'fixture': name, 'stage': stage, 'sample': sample, **measured, 'retention': retention})
                (output/'raw.json').write_text(json.dumps(raw, indent=2)+'\n')
        stages = {}
        for stage in ['old', 'managed']:
            records = [row for row in raw if row['fixture'] == name and row['stage'] == stage]
            stages[stage] = {key: statistics.median(row[key] for row in records) for key in ['TotalMs', 'PeakWorkingSet64', 'AllocatedBytes', 'OutputBytes']}
            stages[stage]['pages'] = sorted({row['Pages'] for row in records})
            stages[stage]['subsetMs'] = statistics.median(row['Metrics'].get('pdfSubset.ms', 0) for row in records)
            stages[stage]['originalFontBytes'] = records[0]['Metrics'].get('pdfOriginalFontBytes', 0)
            stages[stage]['subsetFontBytes'] = records[0]['Metrics'].get('pdfSubsetFontBytes', 0)
            stages[stage]['subsetsCreated'] = records[0]['Metrics'].get('pdfSubsetTypefaceCreated', 0)
        assert stages['old']['pages'] == stages['managed']['pages'], name
        time_ratio = stages['managed']['TotalMs']/stages['old']['TotalMs']
        rss_ratio = stages['managed']['PeakWorkingSet64']/stages['old']['PeakWorkingSet64']
        row = {'fixture': name, 'stages': stages, 'timeRatio': time_ratio, 'peakRssRatio': rss_ratio, 'passed': time_ratio <= 2 and rss_ratio <= 2}
        summary.append(row)
        (output/'summary.json').write_text(json.dumps(summary, indent=2)+'\n')
        print(json.dumps(row), flush=True)
    assert all(row['passed'] for row in summary), summary


if __name__ == '__main__':
    main()

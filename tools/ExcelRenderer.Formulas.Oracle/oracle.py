#!/usr/bin/env python3
"""Generate typed macro-free input workbooks, extract observed caches and compare results.

No command rewrites the checked-in specification/profile expectations.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
FIXTURES = ROOT / 'tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle'
NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
REL = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
ET.register_namespace('', NS)
ET.register_namespace('r', REL)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load_cases(path):
    cases = json.loads(Path(path).read_text(encoding='utf-8'))
    ids = [c['id'] for c in cases]
    if len(ids) != len(set(ids)):
        raise ValueError('duplicate case IDs')
    for c in cases:
        if not re.fullmatch(r'[a-z0-9-]+', c['id']):
            raise ValueError('unsafe case ID')
        if c['expectedSource'] not in ('specification', 'profile', 'hand-calculated'):
            raise ValueError('input expectations cannot claim Excel observations')
    return cases


def cell(address, kind, value):
    element = ET.Element(f'{{{NS}}}c', {'r': address})
    if kind == 'Formula':
        ET.SubElement(element, f'{{{NS}}}f').text = value.lstrip('=')
    elif kind == 'Text':
        element.set('t', 'inlineStr')
        string = ET.SubElement(element, f'{{{NS}}}is')
        ET.SubElement(string, f'{{{NS}}}t', {'{http://www.w3.org/XML/1998/namespace}space': 'preserve'}).text = value
    elif kind != 'Blank':
        element.set('t', {'Number': 'n', 'Boolean': 'b', 'Error': 'e'}[kind])
        ET.SubElement(element, f'{{{NS}}}v').text = ('1' if value else '0') if kind == 'Boolean' else str(value)
    return element


def column(address):
    result = 0
    for char in re.match(r'[A-Z]+', address)[0]:
        result = result * 26 + ord(char) - 64
    return result


def generate(args):
    args.directory.mkdir(parents=True, exist_ok=True)
    metadata = []
    for c in load_cases(args.cases):
        workbook = ET.Element(f'{{{NS}}}workbook')
        ET.SubElement(workbook, f'{{{NS}}}workbookPr', {'date1904': '1' if c['dateSystem'] == 'Excel1904' else '0'})
        sheets = ET.SubElement(workbook, f'{{{NS}}}sheets')
        ET.SubElement(sheets, f'{{{NS}}}sheet', {'name': 'Case', 'sheetId': '1', f'{{{REL}}}id': 'rId1'})
        ET.SubElement(workbook, f'{{{NS}}}calcPr', {'calcId': '0', 'fullCalcOnLoad': '1', 'forceFullCalc': '1'})
        sheet = ET.Element(f'{{{NS}}}worksheet')
        data = ET.SubElement(sheet, f'{{{NS}}}sheetData')
        inputs = list(c['inputs']) + [{'address': c['target'], 'kind': 'Formula', 'value': c['formula']}]
        rows = {}
        for entry in sorted(inputs, key=lambda i: (int(re.search(r'\d+', i['address'])[0]), column(i['address']))):
            row = int(re.search(r'\d+', entry['address'])[0])
            if row not in rows:
                rows[row] = ET.SubElement(data, f'{{{NS}}}row', {'r': str(row)})
            rows[row].append(cell(entry['address'], entry['kind'], entry['value']))
        path = args.directory / (c['id'] + '.xlsx')
        with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as archive:
            archive.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>')
            archive.writestr('_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="'+REL+'/officeDocument" Target="xl/workbook.xml"/></Relationships>')
            archive.writestr('xl/_rels/workbook.xml.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="'+REL+'/worksheet" Target="worksheets/sheet1.xml"/></Relationships>')
            archive.writestr('xl/workbook.xml', ET.tostring(workbook, encoding='utf-8', xml_declaration=True))
            archive.writestr('xl/worksheets/sheet1.xml', ET.tostring(sheet, encoding='utf-8', xml_declaration=True))
        metadata.append({'id': c['id'], 'file': path.name, 'inputSha256': sha(path), 'date1904': c['dateSystem'] == 'Excel1904'})
    (args.directory / 'inputs.json').write_text(json.dumps({'caseSha256': sha(args.cases), 'files': metadata}, indent=2)+'\n', encoding='utf-8')
    print(f'Generated {len(metadata)} typed input workbooks; no observed expectations were changed.')


def prepare_mapping(args):
    args.directory.mkdir(parents=True, exist_ok=True)
    cases, files = [], []
    for count in (0, 1, 3, 70):
        case_id = f'mapping-{count}'
        source = args.samples / f'invoice-{count}' / 'invoice-input.xlsx'
        target = f'D{count+2}'
        output = args.directory / f'{case_id}.xlsx'
        shutil.copyfile(source, output)
        with zipfile.ZipFile(source) as archive:
            root = ET.fromstring(archive.read('xl/worksheets/sheet1.xml'))
            formulas = [c for c in root.iter(f'{{{NS}}}c') if c.find(f'{{{NS}}}f') is not None]
            if any(c.find(f'{{{NS}}}v') is not None for c in formulas):
                raise ValueError('Mapping oracle inputs must have invalidated caches')
            formula = next(c.find(f'{{{NS}}}f').text for c in formulas if c.get('r') == target)
        cases.append(dict(id=case_id, category='mapping', formula=formula, target=target, workbook=True, dateSystem='Excel1900', inputs=[], expected={'kind': 'Number', 'value': count*(count+1)//2*11}, expectedSource='hand-calculated', basis='Invoice quantities 1..n, master price 10, tax 10%; identical cache-free file for both calculators'))
        files.append(dict(id=case_id, file=output.name, inputSha256=sha(output), date1904=False))
    case_path = args.directory / 'cases.json'
    case_path.write_text(json.dumps(cases, indent=2)+'\n', encoding='utf-8')
    (args.directory / 'inputs.json').write_text(json.dumps({'caseSha256': sha(case_path), 'files': files}, indent=2)+'\n', encoding='utf-8')
    print('Prepared four Mapping inputs without changing their formulas or caches.')


def cached(path, target):
    with zipfile.ZipFile(path) as archive:
        root = ET.fromstring(archive.read('xl/worksheets/sheet1.xml'))
        element = next(c for c in root.iter(f'{{{NS}}}c') if c.get('r') == target)
        formula = element.find(f'{{{NS}}}f')
        if formula is None:
            raise ValueError(f'{path}: target formula was lost')
        value = element.find(f'{{{NS}}}v')
        kind = element.get('t', 'n')
        if value is None:
            raise ValueError(f'{path}: formula has no recalculated cache')
        text = value.text or ''
        if kind == 'b':
            return {'kind': 'Boolean', 'value': text == '1'}
        if kind == 'e':
            return {'kind': 'Error', 'value': text}
        if kind == 'str':
            return {'kind': 'Text', 'value': text}
        if kind == 's':
            strings = ET.fromstring(archive.read('xl/sharedStrings.xml'))
            return {'kind': 'Text', 'value': ''.join(strings[int(text)].itertext())}
        if kind != 'n' or not text:
            raise ValueError(f'{path}: unexpected formula cache type {kind}')
        return {'kind': 'Number', 'value': float(text)}


def extract(args):
    manifest = json.loads((args.directory / 'manifest.json').read_text(encoding='utf-8-sig'))
    if manifest['source'] != 'excel-observed' or manifest['caseSha256'].lower() != sha(args.cases):
        raise ValueError('capture manifest source or case hash mismatch')
    by_id = {o['id']: o for o in manifest['observations']}
    observations = []
    for c in load_cases(args.cases):
        if c['id'] not in by_id:
            continue
        m = by_id[c['id']]
        for key in ('excelVersion', 'excelBuild', 'os', 'capturedAt', 'culture', 'compatibilityVersion', 'inputSha256', 'savedSha256', 'date1904'):
            if key not in m:
                raise ValueError('incomplete observation manifest: '+key)
        path = args.directory / (c['id'] + '.xlsx')
        if m['savedSha256'].lower() != sha(path):
            raise ValueError('saved workbook hash mismatch: '+c['id'])
        if m['date1904'] != (c['dateSystem'] == 'Excel1904'):
            raise ValueError('date system mismatch')
        observations.append(dict(m, expected=cached(path, c['target'])))
    args.output.write_text(json.dumps({'source': 'excel-observed', 'caseSha256': sha(args.cases), 'observations': observations}, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(f'Extracted {len(observations)} Excel observations.')


def equal(actual, expected, case):
    if actual['kind'] != expected['kind']:
        return False
    if actual['kind'] == 'Number':
        a, b = actual['value'], expected['value']
        return abs(a-b) <= max(case.get('absTolerance', 0), case.get('relTolerance', 0)*max(abs(a), abs(b)))
    return actual['value'] == expected['value']


def compare(args):
    cases = load_cases(args.cases)
    data = json.loads(args.observed.read_text(encoding='utf-8'))
    if data.get('source') != 'excel-observed':
        raise ValueError('observed input must identify its provenance')
    if data.get('observations') and data.get('caseSha256', '').lower() != sha(args.cases):
        raise ValueError('observations belong to a different input case definition')
    observed = {o['id']: o for o in data['observations']}
    actual = {(r['id'], r['pipeline']): r['actual'] for r in json.loads(args.actual.read_text(encoding='utf-8'))}
    exceptions = {e['id']: e for e in json.loads(args.exceptions.read_text(encoding='utf-8'))}
    counts = Counter({status: 0 for status in ('matched', 'mismatch', 'unsupported', 'known-difference', 'not-observed')})
    profiles, rows, categories = Counter(matched=0, mismatch=0), [], set()
    for c in cases:
        for pipeline in (('mapping', 'roundtrip') if c.get('workbook') else ('engine', 'openxml', 'roundtrip')):
            a = actual.get((c['id'], pipeline))
            if a is None:
                if pipeline in ('engine', 'mapping') or args.require_roundtrip:
                    counts['mismatch'] += 1
                    rows.append({'id': c['id'], 'pipeline': pipeline, 'status': 'mismatch', 'reason': 'pipeline missing'})
                continue
            profiles['matched' if equal(a, c['expected'], c) or (c['expected']['kind'] == 'Diagnostic' and a['kind'] == 'Diagnostic' and c['expected']['value'] in a['value']) else 'mismatch'] += 1
            o = observed.get(c['id'])
            if a['kind'] == 'Diagnostic':
                status = 'unsupported'
            elif o is None or (c['id'] == 'text-len-v1' and str(o.get('compatibilityVersion')) != '1'):
                status = 'not-observed'
            elif equal(a, o['expected'], c):
                status = 'matched'
                categories.add(c['category'])
            else:
                e = exceptions.get(c['id'])
                status = 'known-difference' if e and e.get('reason') and e.get('observed') == o['expected'] and e.get('adopted') == a else 'mismatch'
            counts[status] += 1
            rows.append({'id': c['id'], 'category': c['category'], 'pipeline': pipeline, 'status': status, 'actual': a, 'observed': o['expected'] if o else None})
    required = {c['category'] for c in cases if c['category'] != 'unsupported'}
    missing = sorted(required-categories)
    report = {'caseSha256': sha(args.cases), 'capturedAt': datetime.now(timezone.utc).isoformat(), 'excelObservedCases': len(observed), 'notObservedCases': len(cases)-len(observed), 'counts': dict(counts), 'profileChecks': dict(profiles), 'missingRequiredCategories': missing, 'strict': args.strict, 'rows': rows}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'rows'}, indent=2))
    return 1 if profiles['mismatch'] or counts['mismatch'] or (args.strict and (not observed or missing or counts['not-observed'] or any(c['id'] not in observed for c in cases if c['category'] != 'unsupported'))) else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cases', type=Path, default=FIXTURES/'cases.json')
    sub = parser.add_subparsers(dest='command', required=True)
    g = sub.add_parser('generate'); g.add_argument('directory', type=Path)
    m = sub.add_parser('prepare-mapping'); m.add_argument('samples', type=Path); m.add_argument('directory', type=Path)
    e = sub.add_parser('extract'); e.add_argument('directory', type=Path); e.add_argument('output', type=Path)
    c = sub.add_parser('compare'); c.add_argument('actual', type=Path); c.add_argument('--observed', type=Path, default=FIXTURES/'excel-observed.json'); c.add_argument('--exceptions', type=Path, default=FIXTURES/'compatibility-exceptions.json'); c.add_argument('--output', type=Path, required=True); c.add_argument('--strict', action='store_true'); c.add_argument('--require-roundtrip', action='store_true')
    args = parser.parse_args()
    return {'generate': generate, 'prepare-mapping': prepare_mapping, 'extract': extract, 'compare': compare}[args.command](args) or 0


if __name__ == '__main__':
    sys.exit(main())

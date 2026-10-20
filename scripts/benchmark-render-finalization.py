#!/usr/bin/env python3
"""Extend the existing Performance/CF fixtures with paired, saved-runtime measurements."""
import argparse
import hashlib
import importlib.util
import json
import shutil
import statistics
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]
NS = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
ET.register_namespace('', NS)


def load(name):
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), ROOT / 'scripts' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fixtures(work, runtime):
    directory = work / 'inputs'
    directory.mkdir(parents=True, exist_ok=True)
    cases = []

    def case(name, path, **options):
        cases.append(dict(Name=name, Input=str(path.resolve()), **options))

    def grid(name, pages=1, mode='text', unique=False, symbols=False):
        path = directory / (name + '.xlsx')
        subprocess.run(['dotnet', str(runtime / 'ExcelRenderer.Performance.dll'), 'generate', str(path), str(pages * 20), mode], check=True, capture_output=True)
        with ZipFile(path) as archive:
            parts = {key: archive.read(key) for key in archive.namelist()}
        sheet = ET.fromstring(parts['xl/worksheets/sheet1.xml'])
        columns = sheet.find(f'{{{NS}}}cols')
        for column in columns:
            column.set('width', '3')
        # Fixed row breaks and a narrow grid give genuine 1/10/100 page cases, no fit ambiguity.
        setup = sheet.find(f'{{{NS}}}pageSetup')
        if setup is None:
            setup = ET.SubElement(sheet, f'{{{NS}}}pageSetup')
        setup.set('paperSize', '9')
        if pages > 1:
            breaks = ET.SubElement(sheet, f'{{{NS}}}rowBreaks', count=str(pages - 1), manualBreakCount=str(pages - 1))
            for index in range(1, pages):
                ET.SubElement(breaks, f'{{{NS}}}brk', id=str(index * 20), min='0', max='16383', man='1')
        for cell in sheet.iter(f'{{{NS}}}c'):
            text = cell.find(f'{{{NS}}}is/{{{NS}}}t')
            if text is not None:
                text.text = ('Unique ' + cell.attrib['r'] + ' 日本語長文 e\u0301 123456789') if unique else '辻\U000E0102 \ue000 ABC' if symbols else '反復 ABC'
        parts['xl/worksheets/sheet1.xml'] = ET.tostring(sheet, encoding='utf-8')
        with ZipFile(path, 'w', ZIP_DEFLATED) as archive:
            for key, data in parts.items():
                archive.writestr(key, data)
        return path

    small = grid('small')
    ten = grid('ten', 10)
    hundred = grid('hundred', 100)
    unique = grid('unique', 10, unique=True)
    symbols = grid('symbols', symbols=True)
    images = grid('images', 10, 'images')
    for form in ('Pdf', 'PostScript', 'Png', 'Svg'):
        case('small-' + form, small, Format=form)
    case('ten-Pdf', ten)
    case('hundred-Pdf', hundred)
    case('hundred-PostScript-selected', hundred, Format='PostScript', Pages=[1, 50, 100])
    case('borders-Png', ten, Format='Png')
    case('repeated-Svg', ten, Format='Svg')
    case('unique-Pdf', unique)
    case('range-trim-Pdf', ten, Range='A5:F15', Trim=True)
    for form in ('Png', 'Svg'):
        case('continuous-' + form, ten, Format=form, Continuous=True, Trim=True)
    for form in ('Pdf', 'PostScript'):
        case('symbols-' + form, symbols, Format=form, FontPack=True)
    case('images-PostScript', images, Format='PostScript')
    case('large-image-Png-trim', images, Format='Png', Pages=[1], Trim=True)
    retained = ROOT / 'tests/Shared/RetainedWorkbooks'
    for kind, filename in [('rich', 'RichTextAcceptanceTests.RangeTrimAndMergedCellRetainRichTextInAllOutputFormats.79F50EFE1F4F079D.xlsx'),
                           ('shapes', 'ShapeAndFontTests.CreateWorkbookWithShapes.C704163BF33ECFBD.xlsx')]:
        path = directory / (kind + '.xlsx')
        shutil.copyfile(retained / filename, path)
        for form in ('Pdf', 'PostScript', 'Png', 'Svg'):
            case(kind + '-' + form, path, Format=form, Trim=True)
    phase1 = load('benchmark-conditional-formatting')
    phase2 = load('benchmark-conditional-formatting-phase2')
    for size in (10000, 100000):
        for kind in ('plain', 'phase1', 'shared'):
            path = directory / f'cf-{kind}-{size}.xlsx'
            phase2.population_fixture(path, size, kind, phase1.fixture)
            case(f'read-{kind}-{size}', path, ReadOnly=True)
            if kind == 'shared' and size == 100000:
                case('read-disabled-100000', path, ReadOnly=True, ConditionalFormatting=False)
                case('read-selected-100000', path, ReadOnly=True, Mode='selected')
    for kind, size in [('overlap', 10000), ('mapped', 1), ('recalculate', 10000)]:
        path = directory / f'cf-{kind}.xlsx'
        phase2.population_fixture(path, size, kind, phase1.fixture)
        case('read-' + kind, path, ReadOnly=True, Mode=kind)
    (work / 'cases.json').write_text(json.dumps(cases, ensure_ascii=False, indent=2) + '\n')


def run(args):
    work = args.output.resolve()
    if args.generate:
        fixtures(work, args.before.resolve())
        return
    cases = json.loads((work / 'cases.json').read_text())
    if args.case:
        cases = [case for case in cases if case['Name'] in args.case]
        assert cases, 'No matching case'
    runtimes = {'before': args.before.resolve(), 'after': args.after.resolve()}
    products = {stage: {file.name: sha(file) for file in path.glob('ExcelRenderer*.dll')} for stage, path in runtimes.items()}
    if args.diagnostics:
        observations = []
        for case in cases:
            for stage, runtime in runtimes.items():
                specification = dict(case, Capture=str(work / 'captures' / stage / case['Name']))
                path = work / 'diagnostic-case.json'
                path.write_text(json.dumps([specification]))
                result = subprocess.run(['dotnet', str(runtime / 'ExcelRenderer.Performance.dll'), 'render-profile', str(path), case['Name'], '0', '1', 'diagnostic'], check=True, capture_output=True, text=True)
                observations.append(dict(stage=stage, case=case['Name'], records=[json.loads(line) for line in result.stdout.splitlines()]))
                (work / 'diagnostics.json').write_text(json.dumps(observations, indent=2) + '\n')
                print(f'diagnostic: {case["Name"]}/{stage}', flush=True)
        return
    records = []
    environments = []
    for round_index in range(args.rounds):
        for case in cases:
            for stage in ('before', 'after') if round_index % 2 == 0 else ('after', 'before'):
                command = ['dotnet', str(runtimes[stage] / 'ExcelRenderer.Performance.dll'), 'render-profile', str(work / 'cases.json'), case['Name'], str(args.warmups), str(args.samples)]
                result = subprocess.run(command, check=True, capture_output=True, text=True)
                rows = [json.loads(line) for line in result.stdout.splitlines()]
                environments.append(dict(round=round_index, stage=stage, case=case['Name'], **rows[0]))
                records.extend(dict(round=round_index, stage=stage, **row) for row in rows[1:])
                (work / 'raw.json').write_text(json.dumps(records, indent=2) + '\n')
                (work / 'environments.json').write_text(json.dumps(environments, indent=2) + '\n')
                print(f'{round_index}: {case["Name"]}/{stage}', flush=True)
    summary = []
    for case in cases:
        entry = dict(case=case['Name'])
        for stage in runtimes:
            values = [row for row in records if row['name'] == case['Name'] and row['stage'] == stage and not row['warmup']]
            assert values and len({(row['pages'], row['outputBytes']) for row in values}) == 1, case['Name']
            rounds = [statistics.median(row['milliseconds'] for row in values if row['round'] == index) for index in range(args.rounds)]
            entry[stage] = dict(milliseconds=statistics.median(row['milliseconds'] for row in values), allocationBytes=statistics.median(row['allocationBytes'] for row in values), peakRssBytes=statistics.median(row['peakRssBytes'] for row in values), gc=[statistics.median(row['gc'][generation] for row in values) for generation in range(3)], outputBytes=values[0]['outputBytes'], pages=values[0]['pages'], roundMedians=rounds, minimum=min(row['milliseconds'] for row in values), maximum=max(row['milliseconds'] for row in values), coldMilliseconds=[row['milliseconds'] for row in records if row['name'] == case['Name'] and row['stage'] == stage and row['cold']])
        entry['timeChangePercent'] = (entry['after']['milliseconds'] / entry['before']['milliseconds'] - 1) * 100
        entry['allocationChangePercent'] = (entry['after']['allocationBytes'] / entry['before']['allocationBytes'] - 1) * 100
        entry['pairedRoundChanges'] = [(after / before - 1) * 100 for before, after in zip(entry['before']['roundMedians'], entry['after']['roundMedians'])]
        summary.append(entry)
    assert products == {stage: {file.name: sha(file) for file in path.glob('ExcelRenderer*.dll')} for stage, path in runtimes.items()}
    (work / 'summary.json').write_text(json.dumps(dict(protocol=vars(args) | {'before': str(args.before), 'after': str(args.after), 'output': str(args.output)}, products=products, inputs={file.name: sha(file) for file in (work / 'inputs').glob('*.xlsx')}, summary=summary), indent=2) + '\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--generate', action='store_true')
    parser.add_argument('--diagnostics', action='store_true')
    parser.add_argument('--rounds', type=int, default=3)
    parser.add_argument('--warmups', type=int, default=2)
    parser.add_argument('--samples', type=int, default=3)
    parser.add_argument('--case', action='append')
    run(parser.parse_args())

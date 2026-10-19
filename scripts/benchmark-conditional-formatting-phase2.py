#!/usr/bin/env python3
"""Phase 2 protocol, invoked by benchmark-conditional-formatting.py --phase2."""
import hashlib
import json
import statistics
import subprocess
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


def run(args, parent):
    bench = parent['bench']
    root = parent['ROOT']
    # Reuse the exact Phase 1/Core harness and add only selection, mapping and checksums.
    bench.HARNESS = parent['bench'].HARNESS.replace('var bytes = File.ReadAllBytes(args[0]);', '''var bytes = File.ReadAllBytes(args[0]);
if (args[2].Contains("mapped")) {
    using var template = new MemoryStream(bytes);
    using var mapped = new MemoryStream();
    ExcelRenderer.Mapping.ExcelTemplateMapper.Map(template, mapped, new { items = Enumerable.Range(0, 10000).Select(i => i % 101).ToArray() });
    bytes = mapped.ToArray();
}''').replace('iteration < 15', 'iteration < 8').replace('iteration < 10', 'iteration < 5').replace('int count, painted;', 'int count, painted;\n    string firstColors = "";').replace('var document = new ExcelReader(fonts).Read(input, options);', '''if (mode.Contains("selected")) {
        typeof(ExcelReadOptions).GetProperty("GetSelectedRange", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(options,
            (Func<string, (int, int, int, int)?>)(_ => (1, 1, 5, 1)));
    }
    var document = new ExcelReader(fonts).Read(input, options);
    firstColors = string.Join("|", Enumerable.Range(1, 5).Select(row => document.Sheets[0].Cells.GetValueOrDefault(new(row, 1))?.Style.Background?.ToString() ?? "none"));''').replace('count, painted, metrics', 'count, painted, firstColors, metrics')
    work = args.output.resolve()
    work.mkdir(parents=True, exist_ok=True)
    assert not any(work.iterdir()), 'Use an empty output directory'
    for name in ('Directory.Build.props', 'Directory.Build.targets'):
        (work / name).write_text('<Project/>\n')
    sources = {'before': args.before.resolve(), 'after': args.after.resolve()}
    hashes = {stage: {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('ExcelRenderer*.dll')} for stage, source in sources.items()}
    harnesses = {stage: bench.build_harness(source, work / stage) for stage, source in sources.items()}

    def fixture(name, size, kind):
        path = work / f'{name}.xlsx'
        parent['fixture'](path, 'rules')
        with ZipFile(path) as archive:
            parts = {name: archive.read(name) for name in archive.namelist()}
        rows = []
        for row in range(1, size + 1):
            number = (row - 1) % 101
            if kind == 'mapped':
                cell = '<c r="A1" t="inlineStr"><is><t>**items[*]</t></is></c>'
            elif kind == 'strings':
                cell = f'<c r="A{row}" t="inlineStr"><is><t>key-{number}</t></is></c>'
            else:
                formula = f'<f>MOD({row}-1,101)</f>' if kind == 'recalculate' else ''
                cell = f'<c r="A{row}">{formula}<v>{number}</v></c>'
            rows.append(f'<row r="{row}">{cell}</row>')
        ranges = f'A1:A{size}'
        if kind == 'whole':
            ranges = 'A1:A1048576'
        elif kind == 'overlap':
            ranges = f'A1:A{size} A1:A{size // 2} A{size // 2}:A{size}'
        definitions = {
            'duplicate': '<cfRule type="duplicateValues" priority="1" dxfId="0"/>',
            'top': '<cfRule type="top10" priority="2" rank="10" dxfId="0"/>',
            'average': '<cfRule type="aboveAverage" priority="3" dxfId="0"/>',
            'scale': '<cfRule type="colorScale" priority="4"><colorScale><cfvo type="min"/><cfvo type="percentile" val="50"/><cfvo type="max"/><color rgb="FF000000"/><color rgb="FF00FF00"/><color rgb="FFFFFFFF"/></colorScale></cfRule>',
            'phase1': '<cfRule type="cellIs" operator="greaterThan" priority="1" dxfId="0"><formula>50</formula></cfRule>',
        }
        rules = ''
        keys = [] if kind == 'plain' else [kind] if kind in definitions else ['duplicate'] if kind == 'strings' else ['duplicate', 'top', 'average', 'scale']
        for key in keys:
            rules += f'<conditionalFormatting sqref="{ranges}">{definitions[key]}</conditionalFormatting>'
        if kind == 'distinct':
            rules = ''.join(f'<conditionalFormatting sqref="A{i}:A{size - i + 1}">{definitions["average"].replace("priority=\"3\"", f"priority=\"{i}\"")}</conditionalFormatting>' for i in range(1, 17))
        parts['xl/worksheets/sheet1.xml'] = ('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>' + ''.join(rows) + '</sheetData>' + rules + '</worksheet>').encode()
        with ZipFile(path, 'w', ZIP_DEFLATED) as archive:
            for part, data in parts.items():
                archive.writestr(part, data)

    cases = []
    for size in (10000, 100000):
        for kind in ('plain', 'phase1', 'duplicate', 'strings', 'top', 'average', 'scale', 'shared'):
            name = f'{kind}-{size}'
            fixture(name, size, kind)
            stages = ('before', 'after') if kind in ('plain', 'phase1') else ('after',)
            cases.append((name, 'enabled', stages))
    for name, size, kind, mode in [('whole', 10000, 'whole', 'enabled'), ('overlap', 10000, 'overlap', 'enabled'),
                                  ('distinct', 10000, 'distinct', 'enabled'), ('formula', 10000, 'recalculate', 'enabled'),
                                  ('formula', 10000, 'recalculate', 'recalculate'), ('mapped', 1, 'mapped', 'mapped'),
                                  ('scale-100000', 100000, 'scale', 'selected')]:
        if not (work / f'{name}.xlsx').exists():
            fixture(name, size, kind)
        cases.append((name, mode, ('after',)))
    cases += [('shared-100000', 'disabled', ('before', 'after')), ('shared-100000', 'core', ('before', 'after'))]
    records = []
    for round_index in range(3):
        for name, mode, stages in cases:
            for stage in stages if round_index % 2 == 0 else stages[::-1]:
                result = subprocess.run(['dotnet', str(harnesses[stage]), str(work / f'{name}.xlsx'), str(root / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'), mode], capture_output=True, text=True, check=True)
                samples = [json.loads(line) for line in result.stdout.splitlines()]
                records.extend({'round': round_index, 'fixture': name, 'mode': mode, 'stage': stage, **sample} for sample in samples)
                (work / 'samples.json').write_text(json.dumps(records, indent=2) + '\n')
                print(f'{round_index}: {name}/{mode}/{stage}', flush=True)
    summary = []
    for name, mode, stages in cases:
        for stage in stages:
            values = [r for r in records if r['fixture'] == name and r['mode'] == mode and r['stage'] == stage and not r['warmup']]
            assert len({r['painted'] for r in values}) == 1
            metrics = values[0]['metrics']
            if mode in ('disabled', 'core') or name.startswith('plain-'):
                expected = 0
            elif name.startswith('phase1-'):
                expected = 50 * (int(name.rsplit('-', 1)[1]) // 101)
            elif name.startswith('top-'):
                expected = int(name.rsplit('-', 1)[1]) // 101
            elif name.startswith('average-'):
                expected = 51 * (int(name.rsplit('-', 1)[1]) // 101)
            elif name == 'distinct':
                expected = None
            elif mode == 'selected':
                expected = 5
            else:
                expected = 10000 if name in ('mapped', 'whole', 'overlap', 'formula') else int(name.rsplit('-', 1)[1])
            if expected is not None:
                assert all(r['painted'] == expected for r in values), (name, mode, stage, expected, values[0]['painted'])
            if mode in ('disabled', 'core') or name.startswith(('plain-', 'phase1-')):
                assert metrics.get('conditionalFormatting.populations', 0) == 0
                assert metrics.get('conditionalFormatting.sorts', 0) == 0
            else:
                assert metrics['conditionalFormatting.populations'] == (16 if name == 'distinct' else 1)
                assert metrics.get('conditionalFormatting.sorts', 0) == (0 if name.startswith(('average-', 'strings-', 'duplicate-')) or name == 'distinct' else 1)
            if name == 'scale-100000' and mode == 'selected':
                full = next(r for r in records if r['fixture'] == name and r['mode'] == 'enabled' and r['stage'] == stage)
                assert values[0]['firstColors'] == full['firstColors'], 'Selection changed population colors'
                assert metrics['conditionalFormatting.valueReads'] == 100000
            summary.append({'fixture': name, 'mode': mode, 'stage': stage, **{key: statistics.median(r[key] for r in values) for key in ('elapsedMs', 'allocatedBytes', 'peakRssBytes', 'count', 'painted')}, 'firstColors': values[0]['firstColors'], 'metrics': metrics})
    assert hashes == {stage: {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('ExcelRenderer*.dll')} for stage, source in sources.items()}
    (work / 'summary.json').write_text(json.dumps({'baseSha': '9ac854f97bfeebc511f89f36e6f340376cfb35d8', 'protocol': {'rounds': 3, 'warmups': 5, 'measurements': 3, 'configuration': 'Release', 'concurrency': 1, 'scope': 'read/evaluate; mapping performed before timed reads; no layout/render'}, 'products': hashes, 'harnessSourceSha256': hashlib.sha256(bench.HARNESS.encode()).hexdigest(), 'fixtures': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in work.glob('*.xlsx')}, 'summary': summary}, indent=2) + '\n')
    print(json.dumps(summary, indent=2))

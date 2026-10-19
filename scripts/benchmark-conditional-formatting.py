#!/usr/bin/env python3
"""Measure conditional-formatting reads against saved Release runtimes; --phase2 adds population cases."""
import argparse
import hashlib
import importlib.util
import json
import statistics
import subprocess
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('reader_bench', ROOT / 'scripts/benchmark-reader-split.py')
bench = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bench)
bench.HARNESS = r'''
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
using ExcelRenderer.OpenXml.Reader;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
var bytes = File.ReadAllBytes(args[0]);
var mode = args[2];
var fonts = new FontManager(new() { FontFiles = new[] { args[1] }, AllowSystemFonts = false, UseFontPack = false });
var snapshotObserver = typeof(OpenXmlWorkbookReader).Assembly.GetType("ExcelRenderer.OpenXml.Reader.Diagnostics.OperationMetrics")!.GetProperty("Observer", BindingFlags.Static | BindingFlags.NonPublic)!;
for (var iteration = 0; iteration < 15; iteration++) {
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var metrics = new Dictionary<string, double>();
    ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
    snapshotObserver.SetValue(null, (Action<string, long>)((name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value));
    using var input = new MemoryStream(bytes, false);
    var options = new ExcelReadOptions { FormulaDisplayMode = mode.StartsWith("recalculate", StringComparison.Ordinal) ? FormulaDisplayMode.Recalculate : FormulaDisplayMode.CachedValue, FormulaEvaluationOptions = new() { MaxEvaluationSteps = 30000000, MaxCellVisits = 5000000 } };
    typeof(ExcelReadOptions).GetProperty("ApplyConditionalFormatting")?.SetValue(options, mode != "disabled" && mode != "recalculate-disabled");
    var allocated = GC.GetTotalAllocatedBytes(true);
    var timer = Stopwatch.StartNew();
    int count, painted;
    if (mode == "core") {
        using var font = new ExcelRenderer.Core.Fonts.SingleFontContext(args[1]);
        using var prepared = await ExcelRenderer.Core.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
        var document = new ExcelRenderer.Core.Excel.CoreExcelReader(font).Read(prepared, null);
        count = document.Sheets.Sum(sheet => sheet.Cells.Count);
        painted = document.Sheets.Sum(sheet => sheet.Cells.Values.Count(cell => cell.Style.Background is not null));
    } else {
        var document = new ExcelReader(fonts).Read(input, options);
        count = document.Sheets.Sum(sheet => sheet.Cells.Count);
        painted = document.Sheets.Sum(sheet => sheet.Cells.Values.Count(cell => cell.Style.Background is not null));
    }
    timer.Stop();
    var allocation = GC.GetTotalAllocatedBytes(true) - allocated;
    using var process = Process.GetCurrentProcess();
    Console.WriteLine(JsonSerializer.Serialize(new { iteration, warmup = iteration < 10,
        elapsedMs = timer.Elapsed.TotalMilliseconds, allocatedBytes = allocation,
        peakRssBytes = process.PeakWorkingSet64, count, painted, metrics }));
    ConversionMetrics.Observer = null;
    snapshotObserver.SetValue(null, null);
}
'''


def fixture(path, kind):
    bench.fixture(path, False)
    with ZipFile(path) as archive:
        parts = {name: archive.read(name) for name in archive.namelist()}
    if kind == 'plain':
        return
    rows = []
    for row in range(1, 10001):
        cells = []
        for column in range(6):
            value = -row if column == 3 and row % 2 == 0 else row + column
            formula = f'<f>A{row}*-1</f>' if kind == 'recalculate' and column == 3 else ''
            cells.append(f'<c r="{chr(65 + column)}{row}">{formula}<v>{value}</v></c>')
        rows.append(f'<row r="{row}">{"".join(cells)}</row>')
    rules = '''<conditionalFormatting sqref="A1:A10000"><cfRule type="cellIs" operator="greaterThan" priority="1" dxfId="0"><formula>5000</formula></cfRule></conditionalFormatting>
<conditionalFormatting sqref="B1:B10000"><cfRule type="cellIs" operator="lessThan" priority="2" dxfId="0"><formula>100</formula></cfRule></conditionalFormatting>
<conditionalFormatting sqref="C1:C10000"><cfRule type="cellIs" operator="between" priority="3" dxfId="0"><formula>200</formula><formula>300</formula></cfRule></conditionalFormatting>
<conditionalFormatting sqref="A1:F10000"><cfRule type="expression" priority="4" dxfId="0"><formula>$D1&lt;0</formula></cfRule></conditionalFormatting>'''
    if kind == 'whole-column':
        rules = rules.replace('10000', '1048576')
    if kind == 'overlap':
        rules = ''.join(f'<conditionalFormatting sqref="A1:F10000"><cfRule type="expression" priority="{i}" dxfId="0"><formula>$D1&lt;0</formula></cfRule></conditionalFormatting>' for i in range(1, 17))
    parts['xl/worksheets/sheet1.xml'] = ('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>' + ''.join(rows) + '</sheetData>' + rules + '</worksheet>').encode()
    parts['xl/styles.xml'] = b'<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts><font><name val="Calibri"/><sz val="11"/></font></fonts><fills><fill><patternFill patternType="none"/></fill></fills><borders><border/></borders><cellStyleXfs><xf/></cellStyleXfs><cellXfs><xf fontId="0" fillId="0" borderId="0"/></cellXfs><dxfs><dxf><fill><patternFill patternType="solid"><fgColor rgb="FFFFFF00"/></patternFill></fill></dxf></dxfs></styleSheet>'
    parts['[Content_Types].xml'] = parts['[Content_Types].xml'].replace(b'</Types>', b'<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>')
    parts['xl/_rels/workbook.xml.rels'] = parts['xl/_rels/workbook.xml.rels'].replace(b'</Relationships>', b'<Relationship Id="rIdStyles" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>')
    with ZipFile(path, 'w', ZIP_DEFLATED) as archive:
        for name, data in parts.items():
            archive.writestr(name, data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', type=Path, required=True)
    parser.add_argument('--after', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument("--phase2", action="store_true", help="Run the Phase 2 population and regression protocol")
    parser.add_argument("--warmups", type=int, default=5, help="Phase 2 warm-up reads per process")
    parser.add_argument("--measurements", type=int, default=3, help="Phase 2 measured reads per process")
    parser.add_argument("--rounds", type=int, default=3, help="Phase 2 alternating rounds")
    parser.add_argument("--case", help="Run one Phase 2 fixture for targeted follow-up")
    parser.add_argument("--mode", help="Restrict Phase 2 follow-up to one read policy")
    parser.add_argument("--fixture-source", type=Path, help="Reuse the exact XLSX bytes from a prior Phase 2 matrix")
    args = parser.parse_args()
    if args.phase2:
        import runpy
        runpy.run_path(str(ROOT / "scripts/benchmark-conditional-formatting-phase2.py"))["run"](args, globals())
        return
    work = args.output.resolve()
    work.mkdir(parents=True, exist_ok=True)
    assert not any(work.iterdir()), 'Use an empty output directory'
    for name in ('Directory.Build.props', 'Directory.Build.targets'):
        (work / name).write_text('<Project/>\n')
    sources = {'before': args.before.resolve(), 'after': args.after.resolve()}
    hashes = {stage: {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('ExcelRenderer*.dll')} for stage, source in sources.items()}
    harnesses = {stage: bench.build_harness(source, work / stage) for stage, source in sources.items()}
    for kind in ('plain', 'rules', 'whole-column', 'overlap', 'recalculate'):
        fixture(work / f'{kind}.xlsx', kind)
    cases = [('plain', 'core', ('before', 'after')), ('plain', 'enabled', ('before', 'after'))]
    cases += [(kind, mode, ('after',)) for kind in ('rules', 'whole-column', 'overlap') for mode in ('disabled', 'enabled')]
    cases += [('recalculate', 'recalculate-disabled', ('after',)), ('recalculate', 'recalculate', ('after',))]
    records = []
    for round_index in range(3):
        for kind, mode, stages in cases:
            for stage in stages if round_index % 2 == 0 else stages[::-1]:
                result = subprocess.run(['dotnet', str(harnesses[stage]), str(work / f'{kind}.xlsx'), str(ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'), mode], capture_output=True, text=True, check=True)
                records.extend({'round': round_index, 'fixture': kind, 'mode': mode, 'stage': stage, **json.loads(line)} for line in result.stdout.splitlines())
                (work / 'samples.json').write_text(json.dumps(records, indent=2) + '\n')
                print(f'{round_index}: {kind}/{mode}/{stage}', flush=True)
    summary = []
    for kind, mode, stages in cases:
        for stage in stages:
            values = [r for r in records if r['fixture'] == kind and r['mode'] == mode and r['stage'] == stage and not r['warmup']]
            assert all(r['count'] == (100000 if kind == 'plain' else 60000) for r in values)
            if stage == 'after':
                assert all(r['metrics'].get('calculation.snapshots', 0) == (0 if kind == 'plain' or mode == 'disabled' else 1) for r in values)
                expected_painted = 0 if mode in ('disabled', 'recalculate-disabled', 'core') or kind == 'plain' else 60000 if kind == 'recalculate' else 30000 if kind == 'overlap' else 32599
                assert all(r['painted'] == expected_painted for r in values), (kind, mode, 'Incorrect effective fill count')
                expected_evaluations = 0 if expected_painted == 0 else 960000 if kind == 'overlap' else 90000
                assert all(r['metrics'].get('conditionalFormatting.evaluations', 0) == expected_evaluations for r in values), (kind, mode, 'Incorrect evaluation count')
            summary.append({'fixture': kind, 'mode': mode, 'stage': stage, **{key: statistics.median(r[key] for r in values) for key in ('elapsedMs', 'allocatedBytes', 'peakRssBytes', 'count', 'painted')}, 'metrics': values[len(values) // 2]['metrics']})
    assert hashes == {stage: {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in source.glob('ExcelRenderer*.dll')} for stage, source in sources.items()}
    (work / 'summary.json').write_text(json.dumps({'baseSha': '1c85529ac8662bf766deb09c00445670f21aafd8', 'protocol': {'rounds': 3, 'warmups': 10, 'measurements': 5, 'configuration': 'Release', 'concurrency': 1}, 'products': hashes, 'harnessSourceSha256': hashlib.sha256(bench.HARNESS.encode()).hexdigest(), 'fixtures': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in work.glob('*.xlsx')}, 'summary': summary}, indent=2) + '\n')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()

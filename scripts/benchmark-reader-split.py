#!/usr/bin/env python3
"""Compare unchanged before/after product DLLs with identical reader instrumentation."""
import argparse
import hashlib
import json
import shutil
import statistics
import subprocess
from pathlib import Path
from xml.sax.saxutils import escape
from zipfile import ZipFile, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]
HARNESS = '''
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
var bytes = File.ReadAllBytes(args[0]);
var mode = args[2];
for (var iteration = 0; iteration < 15; iteration++) {
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var metrics = new Dictionary<string, double>();
    ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
    using var input = new MemoryStream(bytes, false);
    var allocated = GC.GetTotalAllocatedBytes(true);
    var timer = Stopwatch.StartNew();
    int count;
    string checksum;
    if (mode == "core-cached") {
        using var font = new ExcelRenderer.Core.Fonts.SingleFontContext(args[1]);
        using var prepared = await ExcelRenderer.OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
        var document = new ExcelRenderer.Core.Excel.CoreExcelReader(font).Read(prepared, null);
        count = document.Sheets.Sum(sheet => sheet.Cells.Count);
        checksum = string.Join("|", document.Sheets.SelectMany(sheet => sheet.Cells.Values).Select(cell => cell.Text));
    } else {
        var fonts = new FontManager(new() { FontFiles = new[] { args[1] }, AllowSystemFonts = false, UseFontPack = false });
        var document = new ExcelReader(fonts).Read(input, new() { FormulaDisplayMode = mode == "main-recalculate" ? FormulaDisplayMode.Recalculate : FormulaDisplayMode.CachedValue });
        count = document.Sheets.Sum(sheet => sheet.Cells.Count);
        checksum = string.Join("|", document.Sheets.SelectMany(sheet => sheet.Cells.Values).Select(cell => cell.Text));
    }
    timer.Stop();
    var allocation = GC.GetTotalAllocatedBytes(true) - allocated;
    using var process = Process.GetCurrentProcess();
    Console.WriteLine(JsonSerializer.Serialize(new { iteration, warmup = iteration < 10,
        elapsedMs = timer.Elapsed.TotalMilliseconds, allocatedBytes = allocation,
        peakRssBytes = process.PeakWorkingSet64, count, metrics,
        textHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(checksum))) }));
    ConversionMetrics.Observer = null;
}
'''


def fixture(path, formulas):
    import runpy
    create = runpy.run_path(str(ROOT / 'scripts/validate-reader-isolation.py'))['create_fixture']
    create(path)
    with ZipFile(path) as archive:
        parts = {name: archive.read(name) for name in archive.namelist()}
    rows = []
    for row in range(1, 5001 if not formulas else 1001):
        cells = ''.join(f'<c r="{chr(65 + column)}{row}">{f"<f>{row}+{column}</f>" if formulas else ""}<v>{0 if formulas else row + column}</v></c>' for column in range(20))
        rows.append(f'<row r="{row}">{cells}</row>')
    parts['xl/worksheets/sheet1.xml'] = ('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>' + ''.join(rows) + '</sheetData></worksheet>').encode()
    with ZipFile(path, 'w', ZIP_DEFLATED) as archive:
        for name, data in parts.items():
            archive.writestr(name, data)


def build_harness(source, target):
    target.mkdir(parents=True)
    references = ''.join(f'<Reference Include="{escape(path.stem)}"><HintPath>{escape(str(path))}</HintPath></Reference>' for path in sorted(source.glob('*.dll')))
    (target / 'bench.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><AssemblyName>ExcelRenderer.Tests</AssemblyName><IsPackable>false</IsPackable></PropertyGroup><ItemGroup>' + references + '</ItemGroup></Project>')
    (target / 'Program.cs').write_text(HARNESS)
    result = subprocess.run(['dotnet', 'build', '-c', 'Release', '-m:1'], cwd=target, capture_output=True, text=True)
    (target / 'build.log').write_text(result.stdout + result.stderr)
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    output = target / 'bin/Release/net10.0'
    for path in (source / 'runtimes/linux-x64/native').glob('*.so'):
        shutil.copy2(path, output / path.name)
    return output / 'ExcelRenderer.Tests.dll'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--before', required=True, type=Path, help='Saved before runtime directory')
    parser.add_argument('--after', required=True, type=Path, help='After runtime directory')
    parser.add_argument('--output', required=True, type=Path, help='Empty evidence directory')
    args = parser.parse_args()
    work = args.output.resolve()
    work.mkdir(parents=True, exist_ok=True)
    assert not any(work.iterdir()), 'Use an empty output directory'
    # Benchmark harnesses are independent consumers of the saved DLLs.
    for build_file in ('Directory.Build.props', 'Directory.Build.targets'):
        (work / build_file).write_text('<Project/>\n')
    fixture(work / 'plain.xlsx', False)
    fixture(work / 'formulas.xlsx', True)
    stages = {'before': args.before.resolve(), 'after': args.after.resolve()}
    products = {stage: {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in directory.glob('ExcelRenderer*.dll')} for stage, directory in stages.items()}
    harnesses = {stage: build_harness(directory, work / stage) for stage, directory in stages.items()}
    records = []
    for round_index in range(3):
        for mode in ('core-cached', 'main-cached', 'main-recalculate'):
            for stage in (('before', 'after') if round_index % 2 == 0 else ('after', 'before')):
                path = work / ('formulas.xlsx' if mode == 'main-recalculate' else 'plain.xlsx')
                result = subprocess.run(['dotnet', str(harnesses[stage]), str(path), str(ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'), mode], text=True, capture_output=True, check=True)
                for line in result.stdout.splitlines():
                    records.append({'stage': stage, 'mode': mode, 'round': round_index, **json.loads(line)})
                (work / 'samples.json').write_text(json.dumps(records, indent=2) + '\n')
                print(f'{round_index} {mode} {stage} passed', flush=True)
    summary = {}
    for mode in ('core-cached', 'main-cached', 'main-recalculate'):
        selected = [record for record in records if record['mode'] == mode and not record['warmup']]
        rows = 1000 if mode == 'main-recalculate' else 5000
        expected = hashlib.sha256('|'.join(str(row + column) for row in range(1, rows + 1) for column in range(20)).encode()).hexdigest().upper()
        assert all(record['textHash'] == expected and record['count'] == rows * 20 for record in selected), (mode, 'Incorrect display values or cell count')
        assert all(record['metrics']['openXmlReaderWorkbook'] == 1 for record in selected), mode
        summary[mode] = {stage: {key: statistics.median(record[key] for record in selected if record['stage'] == stage) for key in ('elapsedMs', 'allocatedBytes', 'peakRssBytes')} for stage in stages}
    for stage, directory in stages.items():
        assert products[stage] == {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in directory.glob('ExcelRenderer*.dll')}, 'Product DLLs changed during measurement'
    (work / 'summary.json').write_text(json.dumps({'protocol': {'freshProcessRounds': 3, 'warmupReads': 10, 'measuredReads': 5, 'concurrency': 1, 'tieredCompilation': 'runtime default'}, 'harnessSourceSha256': hashlib.sha256(HARNESS.encode()).hexdigest(), 'products': products, 'fixtures': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in work.glob('*.xlsx')}, 'summary': summary}, indent=2) + '\n')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()

#!/usr/bin/env python3
"""Publish and run independent Core/Reader consumers without the calculation adapter."""
import argparse
import json
import subprocess
import tempfile
from pathlib import Path
from xml.sax.saxutils import escape
from zipfile import ZipFile, ZIP_DEFLATED

ROOT = Path(__file__).resolve().parents[1]
FORBIDDEN = {'ExcelRenderer.OpenXml', 'ExcelRenderer.Formulas', 'ExcelRenderer.Mapping', 'ExcelRenderer', 'ExcelRenderer.Fonts', 'ExcelRenderer.Tool'}


def create_fixture(path):
    """Keep saved formula types and unresolved shared formulas independent of an evaluator."""
    s = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
    with ZipFile(path, 'w', ZIP_DEFLATED) as archive:
        archive.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>')
        archive.writestr('_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>')
        archive.writestr('xl/workbook.xml', f'<workbook xmlns="{s}" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Saved" sheetId="1" r:id="rId1"/></sheets></workbook>')
        archive.writestr('xl/_rels/workbook.xml.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>')
        archive.writestr('xl/worksheets/sheet1.xml', f'''<worksheet xmlns="{s}"><sheetData><row r="1">
<c r="A1"><f>UNKNOWN()</f><v>999</v></c><c r="B1" t="str"><f>UNKNOWN()</f><v>saved</v></c>
<c r="C1" t="b"><f>UNKNOWN()</f><v>1</v></c><c r="D1" t="e"><f>UNKNOWN()</f><v>#DIV/0!</v></c>
<c r="E1"><f t="shared" si="0" ref="E1:E2">A1+1</f><v>1000</v></c><c r="F1"><f>UNKNOWN()</f></c>
</row><row r="2"><c r="E2"><f t="shared" si="0"/><v>7</v></c></row></sheetData></worksheet>''')


METADATA = '''
var inventories = new Dictionary<string, string[]>();
foreach (var name in new[] { "ExcelRenderer.Core", "ExcelRenderer.OpenXml.Reader" }) {
    var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
    if (!File.Exists(path)) continue;
    using var file = File.OpenRead(path);
    using var pe = new System.Reflection.PortableExecutable.PEReader(file);
    var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
    inventories.Add(name, references);
}
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(inventories));
'''
READER = '''
using var input = File.OpenRead(args[0]);
using var reader = ExcelRenderer.OpenXml.Reader.OpenXmlWorkbookReader.Open(input);
var cells = reader.EnumerateCells(1).ToArray();
var expected = new[] { "999", "saved", "TRUE", "#DIV/0!", "1000", "", "7" };
if (!cells.Select(cell => reader.FormatCell(cell).Text).SequenceEqual(expected)) throw new Exception("Saved display mismatch");
if (cells[0].Value.Type != ExcelRenderer.OpenXml.Reader.Model.Cells.CellValueType.Number ||
    cells[1].Value.Type != ExcelRenderer.OpenXml.Reader.Model.Cells.CellValueType.String ||
    cells[2].Value.Type != ExcelRenderer.OpenXml.Reader.Model.Cells.CellValueType.Boolean ||
    cells[3].Value.Type != ExcelRenderer.OpenXml.Reader.Model.Cells.CellValueType.Error ||
    cells[6].Formula?.MasterFormula != "A1+1") throw new Exception("Typed/shared saved data lost");
if (!reader.FormatCell(cells[5]).Diagnostics.Any(d => d.Code == "FormulaCachedValueMissing")) throw new Exception("Missing cache diagnostic lost");
'''
CORE = '''
using var input = File.OpenRead(args[0]);
using var output = File.Create(args[2]);
var result = await ExcelRenderer.Core.ExcelConverter.ConvertAsync(input, output, new() { FontFilePath = args[1] });
if (result.PageCount < 1 || output.Length < 5 || !result.Diagnostics.Any(d => d.Code == "FormulaCachedValueMissing")) throw new Exception("Saved-value PDF failed");
'''


def validate(work, configuration):
    # Keep generated consumers independent of checkout-local build settings.
    for build_file in ('Directory.Build.props', 'Directory.Build.targets'):
        (work / build_file).write_text('<Project/>\n')
    fixture = work / 'saved.xlsx'
    create_fixture(fixture)
    report = {}
    for kind, name, code in [('reader', 'ExcelRenderer.OpenXml.Reader', READER), ('core', 'ExcelRenderer.Core', CORE)]:
        directory = work / kind
        directory.mkdir()
        project = ROOT / 'src' / name / f'{name}.csproj'
        (directory / 'consumer.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><IsPackable>false</IsPackable></PropertyGroup><ItemGroup><ProjectReference Include="{escape(str(project))}"/></ItemGroup></Project>')
        (directory / 'Program.cs').write_text(code + METADATA)
        def run(*command):
            result = subprocess.run(list(map(str, command)), cwd=directory, text=True, capture_output=True)
            with (directory / 'commands.log').open('a') as log:
                log.write(' '.join(map(str, command)) + '\n' + result.stdout + result.stderr)
            if result.returncode:
                raise RuntimeError(result.stdout + result.stderr)
            return result.stdout
        run('dotnet', 'restore', 'consumer.csproj')
        run('dotnet', 'build', 'consumer.csproj', '-c', configuration, '--no-restore', '-m:1')
        published = directory / 'publish'
        run('dotnet', 'publish', 'consumer.csproj', '-c', configuration, '--no-build', '--no-restore', '-o', published)
        forbidden = FORBIDDEN | ({'ExcelRenderer.Core', 'SkiaSharp', 'SkiaSharp.NativeAssets.Linux'} if kind == 'reader' else set())
        dlls = sorted(path.name for path in published.rglob('*.dll'))
        assert not {name + '.dll' for name in forbidden}.intersection(dlls), (kind, dlls)
        graphs = {}
        for path in [directory / 'obj/project.assets.json', published / 'consumer.deps.json']:
            libraries = sorted(json.loads(path.read_text())['libraries'])
            assert not {library.split('/')[0] for library in libraries}.intersection(forbidden), (path, libraries)
            graphs[str(path.relative_to(work))] = libraries
        references = json.loads(run('dotnet', published / 'consumer.dll', fixture, ROOT / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf', directory / 'saved.pdf').strip().splitlines()[-1])
        for assembly, names in references.items():
            assert not forbidden.intersection(names), (assembly, names)
        if kind == 'core':
            assert (directory / 'saved.pdf').read_bytes().startswith(b'%PDF')
        report[kind] = {'dlls': dlls, 'graphs': graphs, 'assemblyRefs': references, 'execution': 'passed'}
        print(f'{kind}: restore/build/publish, graph, initial output, AssemblyRef and separate-process execution passed', flush=True)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', type=Path)
    parser.add_argument('--configuration', default='Release')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/reader-isolation.json')
    args = parser.parse_args()
    if args.work:
        work = args.work.resolve()
        work.mkdir(parents=True, exist_ok=True)
        assert not any(work.iterdir()), 'Use an empty work directory'
        report = validate(work, args.configuration)
    else:
        with tempfile.TemporaryDirectory(prefix='excelrenderer-reader-isolation-') as temporary:
            report = validate(Path(temporary), args.configuration)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + '\n')


if __name__ == '__main__':
    main()

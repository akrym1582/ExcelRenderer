#!/usr/bin/env python3
"""Restore and execute isolated consumers of the locally packed integration packages."""
import argparse
import json
import os
import pathlib
import subprocess
import zipfile
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('--packages', required=True, type=pathlib.Path)
parser.add_argument('--work', required=True, type=pathlib.Path)
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
packages = args.packages.resolve()
work = args.work.resolve()
work.mkdir(parents=True, exist_ok=True)
runtimes = subprocess.check_output(['dotnet', '--list-runtimes'], text=True)
runtime_path = next(line.rsplit('[', 1)[1].rstrip(']') for line in runtimes.splitlines() if line.startswith('Microsoft.NETCore.App 10.'))
dotnet_root = pathlib.Path(runtime_path).parents[1]
config = work / 'NuGet.Config'
config.write_text(f'<configuration><packageSources><clear/><add key="local" value="{packages}"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="local"><package pattern="ExcelRenderer*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>')
ns = {'n': 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
inventory = {}
for name in ['ExcelRenderer.OpenXml', 'ExcelRenderer', 'ExcelRenderer.Mapping', 'ExcelRenderer.Tool']:
    with zipfile.ZipFile(packages / f'{name}.1.8.0.nupkg') as archive:
        names = archive.namelist()
        nuspec = ET.fromstring(archive.read(next(path for path in names if path.endswith('.nuspec'))))
        dependencies = {entry.attrib['id']: entry.attrib['version'] for entry in nuspec.findall('.//n:dependency', ns)}
        dlls = [path for path in names if path.endswith('.dll')]
        inventory[name] = {'dependencies': dependencies, 'dlls': dlls}
        if name in ['ExcelRenderer', 'ExcelRenderer.Mapping']:
            assert dependencies['ExcelRenderer.OpenXml'] == '1.8.0', dependencies
            assert not any(path.endswith('/ExcelRenderer.OpenXml.dll') for path in dlls)
        if name == 'ExcelRenderer':
            assert any(path.endswith('/ExcelRenderer.Core.dll') for path in dlls)
        if name == 'ExcelRenderer.OpenXml':
            assert 'ClosedXML' not in dependencies
        if name == 'ExcelRenderer.Tool':
            for assembly in ['ExcelRenderer.OpenXml', 'ExcelRenderer.Core', 'ExcelRenderer.Mapping']:
                assert sum(path.endswith(f'/{assembly}.dll') for path in dlls) == 1
(work / 'inventory.json').write_text(json.dumps(inventory, indent=2) + '\n')

def run(*command, cwd=work):
    print('+', *map(str, command), flush=True)
    environment = dict(os.environ, DOTNET_ROOT=str(dotnet_root), NUGET_PACKAGES=str(work / "packages"))
    subprocess.run(list(map(str, command)), cwd=cwd, env=environment, check=True)

fixture = root / 'tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx'
font = root / 'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
programs = {
    'reader': (['ExcelRenderer.OpenXml'], '''using var input = File.OpenRead(args[0]);
using var reader = ExcelRenderer.OpenXml.OpenXmlWorkbookReader.Open(input);
if (!reader.EnumerateCells(1).Any()) throw new Exception("No cells");
Console.WriteLine("Standalone OpenXml reader passed");'''),
    'mapping': (['ExcelRenderer.Mapping'], '''using var book = new ClosedXML.Excel.XLWorkbook();
book.AddWorksheet("data").Cell("A1").Value = "**value";
using var input = new MemoryStream(); book.SaveAs(input); input.Position = 0;
using var output = new MemoryStream();
ExcelRenderer.Mapping.ExcelTemplateMapper.Map(input, output, new { value = 123 }, new() { Backend = ExcelRenderer.Mapping.MappingBackend.OpenXml });
output.Position = 0;
using var reader = ExcelRenderer.OpenXml.OpenXmlWorkbookReader.Open(output);
if (reader.EnumerateCells(1).Single().Value.Number != 123) throw new Exception("Mapping failed");
Console.WriteLine("Standalone Mapping passed");'''),
    'main-core': (['ExcelRenderer'], '''using var input = File.OpenRead(args[0]);
using var output = new MemoryStream();
var result = await ExcelRenderer.Core.ExcelConverter.ConvertAsync(input, output, new() {
    FontFilePath = args[1],
    ReadOptions = new() { Backend = ExcelRenderer.Core.ReaderBackend.OpenXml }
});
if (result.PageCount < 1 || output.Length == 0) throw new Exception("Core failed");
input.Position = 0; output.SetLength(0);
await ExcelRenderer.ExcelConverter.RenderAsync(input, new ExcelRenderer.Rendering.RenderRequest {
    OutputFormat = ExcelRenderer.Rendering.OutputFormat.Markdown,
    ReadOptions = new() { Backend = ExcelRenderer.Excel.ReaderBackend.OpenXml },
    FontOptions = new() { FontFiles = new[] { args[1] }, AllowSystemFonts = false }
}, new ExcelRenderer.Rendering.SingleStreamOutputSink(output));
if (output.Length == 0) throw new Exception("Main failed");
Console.WriteLine("Main package and bundled Core passed");'''),
}
for name, (references, code) in programs.items():
    directory = work / name
    directory.mkdir(exist_ok=True)
    refs = ''.join(f'<PackageReference Include="{package}" Version="1.8.0" />' for package in references)
    (directory / 'consumer.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup>{refs}</ItemGroup></Project>')
    (directory / 'Program.cs').write_text(code)
    run('dotnet', 'restore', '--configfile', config, cwd=directory)
    run('dotnet', 'build', '--no-restore', '-p:UseSharedCompilation=false', cwd=directory)
    run('dotnet', 'run', '--no-build', '--', fixture, font, cwd=directory)
tool = work / 'tool'
if (tool / 'excelrenderer').exists():
    run('dotnet', 'tool', 'uninstall', 'ExcelRenderer.Tool', '--tool-path', tool)
(work / 'tool.md').unlink(missing_ok=True)
run('dotnet', 'tool', 'install', 'ExcelRenderer.Tool', '--version', '1.8.0', '--tool-path', tool, '--configfile', config)
run(tool / 'excelrenderer', '--help')
run(tool / 'excelrenderer', 'md', fixture, '--reader-backend', 'openxml', '-o', work / 'tool.md')
assert (work / 'tool.md').stat().st_size > 0
print('All package consumers passed', flush=True)

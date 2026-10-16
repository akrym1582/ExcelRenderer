#!/usr/bin/env python3
"""Exercise formula calculation from isolated NuGet consumers, never project references."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('--packages', required=True, type=Path)
parser.add_argument('--work', required=True, type=Path)
a = parser.parse_args()
root = Path(__file__).resolve().parents[1]
packages, work = a.packages.resolve(), a.work.resolve()
work.mkdir(parents=True, exist_ok=True)
config = work / 'NuGet.Config'
config.write_text(f'<configuration><packageSources><clear/><add key="local" value="{packages}"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="local"><package pattern="ExcelRenderer*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>')
ns = {'n': 'http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd'}
inventory = {}
for package in ['ExcelRenderer.Formulas', 'ExcelRenderer.OpenXml', 'ExcelRenderer.Mapping', 'ExcelRenderer', 'ExcelRenderer.Tool']:
    with zipfile.ZipFile(packages / f'{package}.1.8.0.nupkg') as z:
        spec = ET.fromstring(z.read(next(n for n in z.namelist() if n.endswith('.nuspec'))))
        deps = {n.attrib['id']: n.attrib['version'] for n in spec.findall('.//n:dependency', ns)}
        dlls = [n for n in z.namelist() if n.endswith('.dll')]
        inventory[package] = {'dependencies': deps, 'dlls': dlls}
        assert not any(n.startswith(('ClosedXML', 'NPOI', 'Antlr')) for n in deps)
        if package == 'ExcelRenderer.Formulas':
            assert not deps, deps
        if package == 'ExcelRenderer.OpenXml':
            assert deps['ExcelRenderer.Formulas'] == '1.8.0', deps
        if package == 'ExcelRenderer':
            assert any(n.endswith('/ExcelRenderer.Core.dll') for n in dlls)
        if package == 'ExcelRenderer.Tool':
            for name in ['Formulas', 'OpenXml', 'Core', 'Mapping']:
                assert sum(n.endswith(f'/ExcelRenderer.{name}.dll') for n in dlls) == 1
(work / 'inventory.json').write_text(json.dumps(inventory, indent=2) + '\n')
runtime_lines = subprocess.check_output(['dotnet', '--list-runtimes'], text=True).splitlines()
runtime_path = next(line.rsplit('[', 1)[1].rstrip(']') for line in runtime_lines if line.startswith('Microsoft.NETCore.App 10.'))
env = dict(os.environ, NUGET_PACKAGES=str(work / 'packages'), DOTNET_ROOT=str(Path(runtime_path).parents[1]))

def run(*cmd, cwd=work):
    print('+', *map(str, cmd), flush=True)
    subprocess.run(list(map(str, cmd)), cwd=cwd, env=env, check=True)

fixture = '''
using var input = new MemoryStream();
using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(input, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, true)) {
    var wb = doc.AddWorkbookPart(); wb.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
    var ws = wb.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
    ws.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(new DocumentFormat.OpenXml.Spreadsheet.SheetData(
        new DocumentFormat.OpenXml.Spreadsheet.Row(
            new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference="A1", CellValue=new("3") },
            new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference="B1", CellValue=new("125") },
            new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference="C1", CellFormula=new("ROUND(A1*B1*1.1,0)"), CellValue=new("999") }) { RowIndex=1 }));
    wb.Workbook.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheets(new DocumentFormat.OpenXml.Spreadsheet.Sheet { Name="Invoice", SheetId=1, Id=wb.GetIdOfPart(ws) }));
    wb.Workbook.Save();
}
input.Position=0;
'''
programs = {
 'engine': (['ExcelRenderer.Formulas'], '''
var source = new ExcelRenderer.Formulas.InMemoryFormulaWorkbook.Builder().AddSheet("S").Build();
var ast = ExcelRenderer.Formulas.FormulaParser.Parse("SUM(2,3)*4").Formula!;
var r = new ExcelRenderer.Formulas.FormulaEngine().Evaluate(ast, source, new("S",1,1));
if (!r.Success || r.Value.Number != 20) throw new Exception("Engine package failure");
'''),
 'adapter': (['ExcelRenderer.OpenXml'], fixture + '''
using var output = new MemoryStream();
ExcelRenderer.OpenXml.WorkbookRecalculator.Recalculate(input,output);
output.Position=0;
using var reader=ExcelRenderer.OpenXml.OpenXmlWorkbookReader.Open(output);
if(reader.EnumerateCells(1).Single(c=>c.Address.Column==3).Value.Number!=413) throw new Exception("Typed cache failure");
File.WriteAllBytes(args[0],output.ToArray());
'''),
 'mapping': (['ExcelRenderer.Mapping'], fixture + '''
using var output=new MemoryStream();
ExcelRenderer.Mapping.ExcelTemplateMapper.Map(input,output,new {},new(){FormulaCalculationMode=ExcelRenderer.OpenXml.FormulaCalculationMode.Recalculate});
output.Position=0;
using var reader=ExcelRenderer.OpenXml.OpenXmlWorkbookReader.Open(output);
if(reader.EnumerateCells(1).Single(c=>c.Address.Column==3).Value.Number!=413) throw new Exception("Mapping package failure");
'''),
 'main-core': (['ExcelRenderer'], fixture + '''
using var output=new MemoryStream();
var core=await ExcelRenderer.Core.ExcelConverter.ConvertAsync(input,output,new(){FontFilePath=args[1],ReadOptions=new(){FormulaDisplayMode=ExcelRenderer.Core.FormulaDisplayMode.Recalculate}});
if(core.PageCount<1 || output.Length==0) throw new Exception("Core recalc failure");
input.Position=0;output.SetLength(0);
await ExcelRenderer.ExcelConverter.RenderAsync(input,new ExcelRenderer.Rendering.RenderRequest{
OutputFormat=ExcelRenderer.Rendering.OutputFormat.Markdown,
ReadOptions=new(){FormulaDisplayMode=ExcelRenderer.Excel.FormulaDisplayMode.Recalculate},
FontOptions=new(){FontFiles=new[]{args[1]},AllowSystemFonts=false}},new ExcelRenderer.Rendering.SingleStreamOutputSink(output));
if(!System.Text.Encoding.UTF8.GetString(output.ToArray()).Contains("413")) throw new Exception("Main recalc failure");
''')}
fixture_path=work/'invoice.xlsx'
font=root/'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
for name,(refs,code) in programs.items():
    directory=work/name;directory.mkdir(exist_ok=True)
    references=''.join(f'<PackageReference Include="{ref}" Version="1.8.0"/>' for ref in refs)
    (directory/'consumer.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup>{references}</ItemGroup></Project>')
    (directory/'Program.cs').write_text(code+'\nConsole.WriteLine("'+name+' passed");\n')
    run('dotnet','restore','--configfile',config,cwd=directory)
    run('dotnet','run','-c','Release','--no-restore','--',fixture_path,font,cwd=directory)
tool=work/'tool'
if (tool/'excelrenderer').exists():
    run('dotnet','tool','uninstall','ExcelRenderer.Tool','--tool-path',tool)
(work/'cli.md').unlink(missing_ok=True)
run('dotnet','tool','install','ExcelRenderer.Tool','--version','1.8.0','--tool-path',tool,'--configfile',config)
run(tool/'excelrenderer','md',fixture_path,'--formula-mode','recalculate','-o',work/'cli.md')
assert '413' in (work/'cli.md').read_text()
print('Five isolated package consumers passed')

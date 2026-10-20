#!/usr/bin/env python3
"""Exercise formula calculation from isolated NuGet consumers, never project references."""
import argparse
import json
import os
import runpy
import shutil
import sys
import zipfile
from pathlib import Path
import subprocess
from package_distribution import check_packages

parser = argparse.ArgumentParser()
parser.add_argument('--packages', required=True, type=Path)
parser.add_argument('--work', required=True, type=Path)
a = parser.parse_args()
root = Path(__file__).resolve().parents[1]
packages, work = a.packages.resolve(), a.work.resolve()
work.mkdir(parents=True, exist_ok=True)
# Isolated consumers must not inherit source-checkout build configuration when
# --work is inside artifacts. These files also stop parent targets from leaking in.
for build_file in ('Directory.Build.props', 'Directory.Build.targets'):
    (work / build_file).write_text('<Project/>\n', encoding='utf-8')
config = work / 'NuGet.Config'
config.write_text(f'<configuration><packageSources><clear/><add key="local" value="{packages}"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="local"><package pattern="ExcelRenderer*"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>', encoding='utf-8')
inventory = check_packages(packages)
versions = {name: record['version'] for name, record in inventory.items()}
(work / 'inventory.json').write_text(json.dumps(inventory, indent=2) + '\n', encoding='utf-8')
runtime_lines = subprocess.check_output(['dotnet', '--list-runtimes'], encoding='utf-8').splitlines()
runtime_path = next(line.rsplit('[', 1)[1].rstrip(']') for line in runtime_lines if line.startswith('Microsoft.NETCore.App 10.'))
env = dict(os.environ, NUGET_PACKAGES=str(work / 'packages'), DOTNET_ROOT=str(Path(runtime_path).parents[1]))

def run(*cmd, cwd=work):
    print('+', *map(str, cmd), flush=True)
    subprocess.run(list(map(str, cmd)), cwd=cwd, env=env, check=True)


consumer_graphs = []


def audit_consumer(directory):
    paths = list(directory.rglob('project.assets.json')) + list(directory.rglob('*.deps.json'))
    assert paths, f'No restored/runtime graph: {directory}'
    for path in paths:
        graph = json.loads(path.read_text(encoding='utf-8'))
        libraries = sorted(graph.get('libraries', {}))
        if directory.name in {'engine', 'adapter', 'mapping', 'namespace-mapping'}:
            assert not {'ExcelRenderer', 'ExcelRenderer.Core', 'ExcelRenderer.Fonts', 'SkiaSharp'}.intersection(name.split('/')[0] for name in libraries), (path, libraries)
        assert not any('pdfsharp' in name.lower().replace('-', '').replace('_', '').replace('.', '') for name in libraries), path
        consumer_graphs.append({'path': str(path.relative_to(work)), 'libraries': libraries})
    (work / 'consumer-graphs.json').write_text(json.dumps(consumer_graphs, indent=2) + '\n', encoding='utf-8')

fixture = '''
using var input = new MemoryStream();
using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Create(input, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook, true)) {
    var wb = doc.AddWorkbookPart(); wb.Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook();
    var ws = wb.AddNewPart<DocumentFormat.OpenXml.Packaging.WorksheetPart>();
    ws.Worksheet = new DocumentFormat.OpenXml.Spreadsheet.Worksheet(new DocumentFormat.OpenXml.Spreadsheet.SheetData(
        new DocumentFormat.OpenXml.Spreadsheet.Row(
            new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference="A1", CellValue=new("3") },
            new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference="B1", CellValue=new("125") },
            new DocumentFormat.OpenXml.Spreadsheet.Cell { CellReference="C1", CellFormula=new("ROUND(SUMIF(A1:A1,3)*VLOOKUP(3,A1:B1,2,FALSE)*1.1,0)"), CellValue=new("999") }) { RowIndex=1 }));
    wb.Workbook.Append(new DocumentFormat.OpenXml.Spreadsheet.Sheets(new DocumentFormat.OpenXml.Spreadsheet.Sheet { Name="Invoice", SheetId=1, Id=wb.GetIdOfPart(ws) }));
    wb.Workbook.Save();
}
input.Position=0;
'''
programs = {
 'engine': (['ExcelRenderer.Mapping'], '''
var source = new ExcelRenderer.Formulas.Data.InMemoryFormulaWorkbook.Builder().AddSheet("S").Build();
var ast = ExcelRenderer.Formulas.Syntax.FormulaParser.Parse("SUM(2,3)*4").Formula!;
var r = new ExcelRenderer.Formulas.Evaluation.FormulaEngine().Evaluate(ast, source, new("S",1,1));
if (!r.Success || r.Value.Number != 20) throw new Exception("Engine package failure");
foreach (var (formula, expected) in new[] { ("ROUNDDOWN(0.29,2)", 0.29), ("ROUNDUP(0.07,2)", 0.07), ("ROUND(1.005,2)", 1.01) }) {
    var result = new ExcelRenderer.Formulas.Evaluation.FormulaEngine().Evaluate(ExcelRenderer.Formulas.Syntax.FormulaParser.Parse(formula).Formula!, source, new("S",1,1));
    if (!result.Success || result.Value.Number != expected) throw new Exception("Decimal rounding package failure");
}
var reportDate = ExcelRenderer.Formulas.Syntax.FormulaParser.Parse("""TEXT(EOMONTH(DATE(2026,1,31),1),"yyyy-mm-dd")""").Formula!;
var dateResult = new ExcelRenderer.Formulas.Evaluation.FormulaEngine().Evaluate(reportDate, source, new("S",1,1));
if (!dateResult.Success || dateResult.Value.Text != "2026-02-28") throw new Exception("Expanded date/text package failure");
var blanks = ExcelRenderer.Formulas.Syntax.FormulaParser.Parse("""COUNTIF(A:A,"")""").Formula!;
var blankResult = new ExcelRenderer.Formulas.Evaluation.FormulaEngine().Evaluate(blanks, source, new("S",1,2));
if (!blankResult.Success || blankResult.Value.Number != 1048576) throw new Exception("Sparse aggregate package failure");
var limited = new ExcelRenderer.Formulas.Evaluation.FormulaEngine().CreateSession(source, new() { MaxStringLength = 8, MaxCachedStringCharacters = 8 });
var text = ExcelRenderer.Formulas.Syntax.FormulaParser.Parse("IF(TRUE,\\\"-\\\",\\\"\\\")");
if (!text.Success || limited.Evaluate(text.Formula!, new("S",1,1)).Value.Text != "-") throw new Exception("Literal token package failure");
var tooLong = limited.Evaluate(ExcelRenderer.Formulas.Syntax.FormulaParser.Parse("\\\"12345\\\"&\\\"6789\\\"").Formula!, new("S",1,1));
if (tooLong.Success || tooLong.Diagnostics[0].Code != "ResourceLimitExceeded") throw new Exception("String limit package failure");
'''),
 'adapter': (['ExcelRenderer.Mapping'], fixture + '''
using var output = new MemoryStream();
ExcelRenderer.OpenXml.Calculation.WorkbookRecalculator.Recalculate(input,output);
output.Position=0;
using var reader=ExcelRenderer.OpenXml.Reader.OpenXmlWorkbookReader.Open(output);
if(reader.EnumerateCells(1).Single(c=>c.Address.Column==3).Value.Number!=413) throw new Exception("Typed cache failure");
File.WriteAllBytes(args[0],output.ToArray());
'''),
 'mapping': (['ExcelRenderer.Mapping'], fixture + '''
using var output=new MemoryStream();
ExcelRenderer.Mapping.ExcelTemplateMapper.Map(input,output,new {},new(){FormulaCalculationMode=ExcelRenderer.OpenXml.Calculation.FormulaCalculationMode.Recalculate});
output.Position=0;
using var reader=ExcelRenderer.OpenXml.Reader.OpenXmlWorkbookReader.Open(output);
if(reader.EnumerateCells(1).Single(c=>c.Address.Column==3).Value.Number!=413) throw new Exception("Mapping package failure");
'''),
 'main-core': (['ExcelRenderer'], fixture + '''
using var output=new MemoryStream();
var options = new ExcelRenderer.Core.PdfExportOptions {
    FontFilePath = args[1],
    Input = new ExcelRenderer.OpenXml.Reader.Input.WorkbookInputOptions { MaxInputBytes = input.Length },
};
if (typeof(ExcelRenderer.Core.PdfExportOptions).GetProperty("Input")!.PropertyType != typeof(ExcelRenderer.OpenXml.Reader.Input.WorkbookInputOptions) ||
    typeof(ExcelRenderer.Core.ExcelConverter).Assembly.GetType("ExcelRenderer.Core.WorkbookInputOptions") is not null) throw new Exception("Core input API migration failure");
var core=await ExcelRenderer.Core.ExcelConverter.ConvertAsync(input,output,options);
if(core.PageCount<1 || output.Length==0) throw new Exception("Core saved-value failure");
input.Position=0;output.SetLength(0);
await ExcelRenderer.ExcelConverter.RenderAsync(input,new ExcelRenderer.Rendering.RenderRequest{
OutputFormat=ExcelRenderer.Rendering.OutputFormat.Markdown,
ReadOptions=new(){FormulaDisplayMode=ExcelRenderer.Excel.FormulaDisplayMode.Recalculate},
FontOptions=new(){FontFiles=new[]{args[1]},AllowSystemFonts=false}},new ExcelRenderer.Rendering.SingleStreamOutputSink(output));
if(!System.Text.Encoding.UTF8.GetString(output.ToArray()).Contains("413")) throw new Exception("Main recalc failure");
foreach (var backend in new[] { new ExcelRenderer.Rendering.RenderRequest().PdfBackend, ExcelRenderer.PdfBackend.SkiaSharp }) {
    input.Position=0;
    using var pdfOutput=new MemoryStream();
    await ExcelRenderer.ExcelConverter.RenderAsync(input,new(){
        OutputFormat=ExcelRenderer.Rendering.OutputFormat.Pdf, PdfBackend=backend,
        FontOptions=new(){FontFiles=new[]{args[1]},AllowSystemFonts=false,UseFontPack=false}},
        new ExcelRenderer.Rendering.SingleStreamOutputSink(pdfOutput));
    if(pdfOutput.Length==0 || !System.Text.Encoding.ASCII.GetString(pdfOutput.ToArray()).StartsWith("%PDF"))
        throw new Exception("Package-only PDF backend failure: " + backend);
}
''')}
programs['combined'] = (['ExcelRenderer', 'ExcelRenderer.Mapping'], programs['main-core'][1] + """
input.Position = 0;
using var mappedOutput = new MemoryStream();
ExcelRenderer.Mapping.ExcelTemplateMapper.Map(input, mappedOutput, new {}, new() {
    FormulaCalculationMode = ExcelRenderer.OpenXml.Calculation.FormulaCalculationMode.Recalculate
});
mappedOutput.Position = 0;
using var mappedReader = ExcelRenderer.OpenXml.Reader.OpenXmlWorkbookReader.Open(mappedOutput);
if (mappedReader.EnumerateCells(1).Single(c => c.Address.Column == 3).Value.Number != 413)
    throw new Exception("Combined package failure");
""")
# Verify the Phase 2 shared Reader/OpenXml APIs and main evaluator from package assemblies.
programs['conditional-formatting'] = (['ExcelRenderer', 'ExcelRenderer.Mapping'], fixture + '''
using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true)) {
    var ws = doc.WorkbookPart!.WorksheetParts.Single().Worksheet;
    var cells = ws.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().ToArray();
    cells[0].CellValue = new("0"); cells[1].CellValue = new("50");
    cells[2].CellFormula = new("50+50"); cells[2].CellValue = new("100");
    ws.InnerXml += """<conditionalFormatting xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" sqref="A1:C1"><cfRule type="colorScale" priority="1"><colorScale><cfvo type="min"/><cfvo type="max"/><color indexed="0"/><color rgb="FFFFFFFF"/></colorScale></cfRule></conditionalFormatting>""";
}
input.Position = 0;
using (var reader = ExcelRenderer.OpenXml.Reader.OpenXmlWorkbookReader.Open(input)) {
    var calculation = ExcelRenderer.OpenXml.Calculation.WorkbookCalculationSession.CreateSavedValueSession(reader);
    if (calculation.EnumerateValueCells(new("Invoice", 1, 1, 1048576, 16384)).Count() != 3) throw new Exception("Sparse package API failure");
    var scale = reader.ReadWorksheetInfo(1).ConditionalFormattingRules.Single().Source.Children.Single();
    if (scale.Children.First(c => c.Name == "color").Attributes["resolvedRgb"] != "000000") throw new Exception("Scale package metadata failure");
}
input.Position = 0;
using var mapped = new MemoryStream();
ExcelRenderer.Mapping.ExcelTemplateMapper.Map(input, mapped, new {}, new() { FormulaCalculationMode = ExcelRenderer.OpenXml.Calculation.FormulaCalculationMode.Recalculate });
mapped.Position = 0;
var sheet = new ExcelRenderer.Excel.ExcelReader().Read(mapped).Sheets[0];
if (sheet.Cells[new(1,2)].Style.Background != new ExcelRenderer.Model.ReportColor(128,128,128)) throw new Exception("Phase 2 package evaluator failure");
''')
# Exercise Japanese/IVS/color and a nonzero TTC face from package assemblies only.
programs['managed-fonts'] = (['ExcelRenderer', 'ExcelRenderer.Fonts'], fixture + '''
_ = typeof(ExcelRenderer.Fonts.FontPack);
using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true)) {
    var cell = doc.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().First();
    cell.RemoveAllChildren();
    cell.DataType = DocumentFormat.OpenXml.Spreadsheet.CellValues.InlineString;
    cell.InlineString = new(new DocumentFormat.OpenXml.Spreadsheet.Text("日本語 辻󠄂辻󠄃葛󠄂 😀"));
}
input.Position = 0;
using (var pdf = new MemoryStream()) {
    await ExcelRenderer.ExcelConverter.RenderAsync(input, new() {
        OutputFormat = ExcelRenderer.Rendering.OutputFormat.Pdf,
        FontOptions = new() { AllowSystemFonts = false, IvsFontStyle = ExcelRenderer.Fonts.IvsFontStyle.Mincho }
    }, new ExcelRenderer.Rendering.SingleStreamOutputSink(pdf));
    File.WriteAllBytes("japanese-ivs-color.pdf", pdf.ToArray());
}
using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(input, true)) {
    var cell = doc.WorkbookPart!.WorksheetParts.First().Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Cell>().First();
    cell.InlineString = new(new DocumentFormat.OpenXml.Spreadsheet.Text("A B"));
}
input.Position = 0;
using (var pdf = new MemoryStream()) {
    await ExcelRenderer.ExcelConverter.RenderAsync(input, new() {
        OutputFormat = ExcelRenderer.Rendering.OutputFormat.Pdf,
        FontOptions = new() { AllowSystemFonts = false, UseFontPack = false,
            FontFiles = new[] { args[2] }, FontFileCollectionIndex = 1,
            FallbackFamilies = new[] { "PDF Format Fixture" } }
    }, new ExcelRenderer.Rendering.SingleStreamOutputSink(pdf));
    File.WriteAllBytes("collection-face-1.pdf", pdf.ToArray());
}
input.Position = 0;
using (var pdf = new MemoryStream()) {
    await ExcelRenderer.Core.ExcelConverter.ConvertAsync(input, pdf, new() { FontFilePath = args[2], FontCollectionIndex = 1 });
    File.WriteAllBytes("core-collection-face-1.pdf", pdf.ToArray());
}
''')
# Keep the documented low-level sample executable against each supported package
# choice, rather than relying only on source-project references.
namespace_sample = (root / 'samples/namespace-migration/Program.cs').read_text(encoding='utf-8')
programs['namespace-main'] = (['ExcelRenderer'], namespace_sample)
programs['namespace-mapping'] = (['ExcelRenderer.Mapping'], namespace_sample)
programs['namespace-combined'] = (['ExcelRenderer', 'ExcelRenderer.Mapping'], namespace_sample)
fixture_path=work/'invoice.xlsx'
font=root/'third_party/NotoSansJP/NotoSansJP-Regular.ttf'
for name,(refs,code) in programs.items():
    directory=work/name;directory.mkdir(exist_ok=True)
    references=''.join(f'<PackageReference Include="{ref}" Version="{versions[ref]}"/>' for ref in refs)
    (directory/'consumer.csproj').write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup>{references}</ItemGroup></Project>', encoding='utf-8')
    (directory/'Program.cs').write_text(code+'\nConsole.WriteLine("'+name+' passed");\n', encoding='utf-8')
    run('dotnet','restore','--configfile',config,cwd=directory)
    arguments = [] if name.startswith('namespace-') else [fixture_path, font, root / 'tests/ExcelRenderer.Tests/Fonts/PdfFormats/collection-distinct.ttc']
    run('dotnet','run','-c','Release','--no-restore','--',*arguments,cwd=directory)
    audit_consumer(directory)
    # Every consumer copied all native RIDs (including large debug symbols).
    # Keep its source/log evidence but release disposable build products before
    # the next isolated project; this also fits hosted CI's disk budget.
    for generated in ('bin', 'obj'):
        shutil.rmtree(directory / generated, ignore_errors=True)
tool=work/'tool'
tool_command = tool / ('excelrenderer.exe' if os.name == 'nt' else 'excelrenderer')
if tool_command.exists():
    run('dotnet','tool','uninstall','ExcelRenderer.Tool','--tool-path',tool)
(work/'cli.md').unlink(missing_ok=True)
run('dotnet','tool','install','ExcelRenderer.Tool','--version',versions['ExcelRenderer.Tool'],'--tool-path',tool,'--configfile',config)
run(tool_command,'md',fixture_path,'--formula-mode','recalculate','-o',work/'cli.md')
assert '413' in (work/'cli.md').read_text(encoding='utf-8')
for command in ('pdf', 'render'):
    destination = work / f'cli-{command}-skia.pdf'
    destination.unlink(missing_ok=True)
    extra = ['--format', 'pdf'] if command == 'render' else []
    run(tool_command, command, fixture_path, '-o', destination, '--pdf-backend', 'skiasharp', *extra)
    assert destination.read_bytes().startswith(b'%PDF')

# The custom native subset adapter is absent; default PDF must still succeed.
assert not list(tool.rglob('*HarfBuzzSubset*'))
default_output = work / 'cli-default-without-subset.pdf'
default_output.unlink(missing_ok=True)
run(tool_command, 'pdf', fixture_path, '-o', default_output)
assert default_output.read_bytes().startswith(b'%PDF')
audit_consumer(tool)
print('Eleven isolated package consumers passed (engine, adapter, Mapping, main+Core, combined, conditional formatting, managed fonts, three namespace samples, installed CLI)')

# Make the requested family explicit so the installed CLI must use the chosen
# collection/CFF/variable face rather than an available bundled fallback.
font_fixture = work / 'cli-font-formats.xlsx'
runpy.run_path(str(root / 'scripts/benchmark-managed-fonts.py'))['workbook'](font_fixture, 'A B')
with zipfile.ZipFile(font_fixture) as archive:
    font_entries = {name: archive.read(name) for name in archive.namelist()}
font_entries['xl/styles.xml'] = font_entries['xl/styles.xml'].replace(b'Noto Sans JP', b'PDF Format Fixture')
with zipfile.ZipFile(font_fixture, 'w', zipfile.ZIP_DEFLATED) as archive:
    for name, data in font_entries.items():
        archive.writestr(name, data)
collection_output = work / 'cli-collection-face-1.pdf'
collection_output.unlink(missing_ok=True)
run(tool_command, 'pdf', font_fixture, '-o', collection_output,
    '--font-file', root / 'tests/ExcelRenderer.Tests/Fonts/PdfFormats/collection-distinct.ttc',
    '--font-face-index', '1', '--fallback-font', 'PDF Format Fixture', '--no-system-fonts')
assert b'PdfFormatFixture-Bold' in collection_output.read_bytes()
for name, reason in [('cff.otf', 'CFF'), ('variable.ttf', 'variable')]:
    destination = work / ('cli-rejected-' + name + '.pdf')
    destination.unlink(missing_ok=True)
    command = [str(tool_command), 'pdf', str(font_fixture), '-o', str(destination),
        '--font-file', str(root / 'tests/ExcelRenderer.Tests/Fonts/PdfFormats' / name),
        '--font-face-index', '0', '--fallback-font', 'PDF Format Fixture', '--no-system-fonts']
    rejected = subprocess.run(command, env=env, encoding='utf-8', capture_output=True)
    assert rejected.returncode != 0 and reason in rejected.stderr, (name, rejected.stderr)
    assert not destination.exists(), 'Unsupported font committed an installed-CLI PDF'
print('Installed CLI used collection face 1 and rejected CFF/variable fonts without committing output')

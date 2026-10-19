#!/usr/bin/env python3
"""Measure XLSX mapping in fresh processes (.NET 10 and Linux /proc required)."""
import argparse
import hashlib
import json
import pathlib
import subprocess
import platform
import tempfile
import time
from xml.sax.saxutils import escape

SOURCE = r'''
using System.Diagnostics;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader;
using S = DocumentFormat.OpenXml.Spreadsheet;
using ExcelRenderer.Mapping;
using ExcelRenderer.Mapping.Diagnostics;

string mode = args[0];
int count = int.Parse(args[1]);
int columns = args.Length > 2 ? int.Parse(args[2]) : 10;
var data = new { items = Enumerable.Range(0, mode == "clr" ? 0 : count).Select(i => Enumerable.Range(0, columns).ToDictionary(c => "v" + c, c => (object)(c == 0 ? "item-" + i : i + c))).ToArray() };
object? root = data;
if (mode == "nested") root = new { groups = Enumerable.Range(0, count / 10).Select(g => new { items = data.items.Skip(g * 10).Take(10).ToArray() }).ToArray() };
using var template = new MemoryStream();
using (var package = SpreadsheetDocument.Create(template, SpreadsheetDocumentType.Workbook, true))
{
    var workbook = package.AddWorkbookPart();
    workbook.Workbook = new S.Workbook(new S.Sheets());
    var part = workbook.AddNewPart<WorksheetPart>();
    workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(part), SheetId = 1, Name = "Template" });
    var dataRows = new S.SheetData();part.Worksheet = new S.Worksheet(dataRows);
    workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = new S.Stylesheet(
        new S.Fonts(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Calibri" })),
        new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 }),new S.Fill(new S.PatternFill(new S.ForegroundColor { Rgb = "FFADD8E6" }) { PatternType = S.PatternValues.Solid })),
        new S.Borders(new S.Border()),new S.CellStyleFormats(new S.CellFormat()),new S.CellFormats(new S.CellFormat(),new S.CellFormat { FillId = 2,ApplyFill = true }),new S.CellStyles(new S.CellStyle { Name = "Normal",FormatId = 0,BuiltinId = 0 }));
    void Text(int row,int column,string text,uint style = 0)
    {
        var record = dataRows.Elements<S.Row>().SingleOrDefault(item=>item.RowIndex?.Value==(uint)row);
        if(record is null){record = new S.Row { RowIndex = (uint)row };dataRows.Append(record);}
        record.Append(new S.Cell(new S.InlineString(new S.Text(text))) { CellReference = new CellAddress(row,column).ToString(),DataType = S.CellValues.InlineString,StyleIndex = style });
    }
    int row = 2;Text(1,1,"Header");
    if(mode is "block" or "nested")
    {
        Text(row++,1,mode == "nested" ? "**@start-array groups[*] as group" : "**@start-array items[*] as item");
        if(mode == "nested")Text(row++,1,"**@start-array @group.items[*] as item");
    }
    for(int column = 0;column < columns;column++)Text(row,column+1,(mode is "block" or "nested" ? "**@item." : "**items[*].")+"v"+column,1);
    var repeated = dataRows.Elements<S.Row>().Single(item=>item.RowIndex?.Value==(uint)row);repeated.Height = 22;repeated.CustomHeight = true;
    if(mode is "merged" or "merged-contained")
    {
        Text(row,columns+1,"Merged");
        part.Worksheet.Append(new S.MergeCells(new S.MergeCell { Reference = new CellAddress(row,columns+1)+":"+new CellAddress(row,columns+2) }));
        if(mode == "merged-contained")Text(row,columns+3,"After merge");
    }
    if(mode == "formula")repeated.Append(new S.Cell(new S.CellFormula("B"+row+"*2")) { CellReference = new CellAddress(row,columns+1).ToString() });
    row++;
    if(mode is "block" or "nested")Text(row++,1,"**@end-array");
    if(mode == "nested")Text(row++,1,"**@end-array");
    Text(row,1,"Footer");
}
string? json = mode == "json" ? JsonSerializer.Serialize(data) : null;
if (mode == "json") { root = null; data = null!; }
if (mode == "clr") root = new { items = Enumerable.Range(0, count).Select(i => new Row { v0 = "item-" + i, v1 = i + 1, v2 = i + 2, v3 = i + 3, v4 = i + 4, v5 = i + 5, v6 = i + 6, v7 = i + 7, v8 = i + 8, v9 = i + 9 }).ToArray() };
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
long before = GC.GetTotalAllocatedBytes(true);
using var output = new MemoryStream();
template.Position = 0;
var sw = Stopwatch.StartNew();
try
{
    if (json != null) ExcelTemplateMapper.MapJson(template, output, json);
    else ExcelTemplateMapper.Map(template, output, root);
    sw.Stop();
    long allocated = GC.GetTotalAllocatedBytes(true) - before;
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "mapped", mode, count, columns, elapsedMs = sw.Elapsed.TotalMilliseconds, allocatedBytes = allocated, outputBytes = output.Length, peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64 }));
    Console.Out.Flush();
    output.Position = 0;
    using var reader = OpenXmlWorkbookReader.Open(output);
    var cells = reader.EnumerateCells(1).ToDictionary(cell=>cell.Address);
    var info = reader.ReadWorksheetInfo(1);
    if(info.ActualCellRange!.Last.Row != count+2 || cells[new(2,1)].Value.Text != "item-0" || cells[new(count+1,1)].Value.Text != "item-"+(count-1) || cells[new(count+2,1)].Value.Text != "Footer")throw new Exception("Output mismatch");
    if(mode == "formula")throw new Exception("Repeated formulas should have been rejected");
    if((mode is "merged" or "merged-contained") && info.MergedRanges.Count != count)throw new Exception("Merge mismatch");
    var rows = info.Rows.ToDictionary(row=>row.Index);
    var merges = info.MergedRanges.Select(range=>(range.First.Row,range.Last.Row,range.First.Column,range.Last.Column)).ToHashSet();
    for(int item=0;item<count;item++)
    {
        int row=item+2;var first=cells[new(row,1)];
        if(first.Value.Type != CellValueType.String || first.Value.Text != "item-"+item)throw new Exception("Text mismatch");
        for(int column=2;column<=columns;column++)if(cells[new(row,column)].Value.Type != CellValueType.Number || cells[new(row,column)].Value.Number != item+column-1)throw new Exception("Number mismatch");
        var fill=reader.ResolveStyle(first).Fill.Children.Single().Children.Single().Attributes["rgb"];
        if(rows[row].HeightPt!=22 || fill!="FFADD8E6")throw new Exception("Style mismatch");
        if((mode is "merged" or "merged-contained") && !merges.Contains((row,row,columns+1,columns+2)))throw new Exception("Merge mismatch");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "verified", mode, count }));
}
catch (MappingException ex)
{
    sw.Stop();
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "rejected", mode, count, elapsedMs = sw.Elapsed.TotalMilliseconds, outputBytes = output.Length, message = ex.Message }));
    bool expected = mode == "formula" ? ex.ReasonCode == "MappingFormulaNotSupported" : count >= 100000 && ex.Message.Contains("MaxOutputRows");
    if (!expected || output.Length != 0) throw;
}
catch (Exception ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "failed", mode, count, outputBytes = output.Length, message = ex.Message }));
    throw;
}
public sealed class Row
{
    public string v0 { get; set; } = "";
    public int v1 { get; set; }
    public int v2 { get; set; }
    public int v3 { get; set; }
    public int v4 { get; set; }
    public int v5 { get; set; }
    public int v6 { get; set; }
    public int v7 { get; set; }
    public int v8 { get; set; }
    public int v9 { get; set; }
}
'''

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--output", type=pathlib.Path, required=True)
parser.add_argument("--mode", choices=["clr", "json", "dict", "block", "nested", "merged", "merged-contained", "formula"])
parser.add_argument("--rows", type=int, default=1000)
parser.add_argument("--columns", type=int, default=10)
parser.add_argument("--runs", type=int, default=3)
parser.add_argument("--timeout", type=float, default=90)
args = parser.parse_args()
if args.rows < 1 or args.columns < 2 or args.runs < 1 or args.timeout <= 0:
    parser.error("rows/runs/timeout must be positive; columns must be at least 2")
if args.mode == "clr" and args.columns != 10:
    parser.error("CLR fixture has exactly 10 properties")
if args.mode == "nested" and args.rows % 10:
    parser.error("nested rows must be a multiple of 10")
repo = pathlib.Path(__file__).resolve().parents[2]
project = repo / "src/ExcelRenderer.Mapping/ExcelRenderer.Mapping.csproj"
cases = [(args.mode, args.rows, args.columns)] if args.mode else [
    (m, n, 10) for m in ("clr", "json") for n in (1000, 10000, 50000, 99998)
] + [("dict", 10000, 50)] + [
    (m, n, 10) for m in ("nested", "merged", "merged-contained", "formula") for n in (1000, 10000)
] + [("block", n, 10) for n in (100, 300, 1000)] + [("clr", 100000, 10)]
args.output.parent.mkdir(parents=True, exist_ok=True)
metadata = {
    "commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip(),
    "workingTreeDirty": bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=repo, text=True).strip()),
    "sourceHashes": {
        path.relative_to(project.parent).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(project.parent.rglob("*.cs"))
        if not {"obj", "bin"}.intersection(path.relative_to(project.parent).parts)
    },
    "fixtureSha256": hashlib.sha256(SOURCE.encode()).hexdigest(),
    "platform": platform.platform(),
    "dotnet": subprocess.check_output(["dotnet", "--info"], text=True),
    "sampleIntervalMs": 10, "timeoutSeconds": args.timeout,
    "runs": args.runs, "cases": cases,
}
for name in ("cpu.max", "memory.max"):
    limit = pathlib.Path("/sys/fs/cgroup") / name
    metadata[name] = limit.read_text().strip() if limit.exists() else None
args.output.with_suffix(".metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
with tempfile.TemporaryDirectory(prefix="mapping-perf-") as tmp:
    directory = pathlib.Path(tmp)
    (directory / "Program.cs").write_text(SOURCE)
    (directory / "MappingPerf.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
        '<Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="'
        + escape(str(project), {'"': '&quot;'}) + '" /><ProjectReference Include="'
        + escape(str(repo / 'src/ExcelRenderer.OpenXml/ExcelRenderer.OpenXml.csproj'), {'"': '&quot;'}) + '" /></ItemGroup></Project>')
    subprocess.run(["dotnet", "build", str(directory / "MappingPerf.csproj"), "-c", "Release", "--verbosity", "minimal"], check=True)
    dll = directory / "bin/Release/net10.0/MappingPerf.dll"
    failures = 0
    for run in range(args.runs):
        for mode, rows, columns in cases:
            stdout_path = directory / "stdout.jsonl"
            stderr_path = directory / "stderr.txt"
            with stdout_path.open("w") as stdout, stderr_path.open("w") as stderr:
                process = subprocess.Popen(
                    ["dotnet", str(dll), mode, str(rows), str(columns)],
                    stdout=stdout, stderr=stderr)
                started = time.monotonic()
                peak_rss = 0
                mapped = False
                timed_out = False
                while process.poll() is None:
                    mapped = mapped or '\"phase\":\"mapped\"' in stdout_path.read_text()
                    if not mapped:
                        try:
                            status = pathlib.Path(f"/proc/{process.pid}/status").read_text()
                            for line in status.splitlines():
                                if line.startswith("VmRSS:"):
                                    peak_rss = max(peak_rss, int(line.split()[1]) * 1024)
                        except FileNotFoundError:
                            pass
                    if time.monotonic() - started > args.timeout:
                        timed_out = True
                        process.kill()
                        break
                    time.sleep(0.01)
                process.wait()
            records = [json.loads(line) for line in stdout_path.read_text().splitlines()]
            successful = process.returncode == 0 and any(
                r["phase"] in ("verified", "rejected") for r in records)
            failures += not successful
            record = {
                "run": run, "mode": mode, "count": rows, "columns": columns,
                "exitCode": process.returncode, "timeout": timed_out,
                "mappingPeakRssBytes": peak_rss,
                "wallSeconds": time.monotonic() - started,
                "records": records, "stderr": stderr_path.read_text(),
            }
            with args.output.open("a") as output:
                output.write(json.dumps(record) + "\n")
            print(json.dumps(record), flush=True)
    raise SystemExit(1 if failures else 0)

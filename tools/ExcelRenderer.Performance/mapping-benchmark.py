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
using ClosedXML.Excel;
using ExcelRenderer.Mapping;

string mode = args[0];
int count = int.Parse(args[1]);
int columns = args.Length > 2 ? int.Parse(args[2]) : 10;
var data = new { items = Enumerable.Range(0, mode == "clr" ? 0 : count).Select(i => Enumerable.Range(0, columns).ToDictionary(c => "v" + c, c => (object)(c == 0 ? "item-" + i : i + c))).ToArray() };
object? root = data;
if (mode == "nested") root = new { groups = Enumerable.Range(0, count / 10).Select(g => new { items = data.items.Skip(g * 10).Take(10).ToArray() }).ToArray() };
using var template = new MemoryStream();
using (var book = new XLWorkbook())
{
    var s = book.AddWorksheet("Template");
    int row = 2;
    s.Cell(1, 1).Value = "Header";
    if (mode == "block" || mode == "nested")
    {
        s.Cell(row++, 1).Value = mode == "nested" ? "**@start-array groups[*] as group" : "**@start-array items[*] as item";
        if (mode == "nested") s.Cell(row++, 1).Value = "**@start-array @group.items[*] as item";
    }
    for (int c = 0; c < columns; c++) s.Cell(row, c + 1).Value = (mode == "block" || mode == "nested" ? "**@item." : "**items[*].") + "v" + c;
    s.Range(row, 1, row, columns).Style.Fill.BackgroundColor = XLColor.LightBlue;
    s.Row(row).Height = 22;
    if (mode == "merged" || mode == "merged-contained")
    {
        s.Range(row, columns + 1, row, columns + 2).Merge().FirstCell().Value = "Merged";
        if (mode == "merged-contained") s.Cell(row, columns + 3).Value = "After merge";
    }
    if (mode == "formula") s.Cell(row, columns + 1).FormulaA1 = "B" + row + "*2";
    row++;
    if (mode == "block" || mode == "nested") s.Cell(row++, 1).Value = "**@end-array";
    if (mode == "nested") s.Cell(row++, 1).Value = "**@end-array";
    s.Cell(row, 1).Value = "Footer";
    book.SaveAs(template);
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
    using var result = new XLWorkbook(output);
    var s = result.Worksheet(1);
    if (s.LastRowUsed()!.RowNumber() != count + 2 || s.Cell(2, 1).GetString() != "item-0" || s.Cell(count + 1, 1).GetString() != "item-" + (count - 1) || s.Cell(count + 2, 1).GetString() != "Footer" || s.Cell(count + 1, 2).GetDouble() != count || s.Row(count + 1).Height != 22 || s.Cell(count + 1, 1).Style.Fill.BackgroundColor != XLColor.LightBlue) throw new Exception("Output mismatch");
    if ((mode == "merged" || mode == "merged-contained") && s.MergedRanges.Count() != count) throw new Exception($"Merge mismatch: expected {count}, got {s.MergedRanges.Count()}");
    if (mode == "formula") throw new Exception("A formula inside a repeated row should have been rejected");
    var merges = s.MergedRanges.Select(range => (range.RangeAddress.FirstAddress.RowNumber, range.RangeAddress.LastAddress.RowNumber, range.RangeAddress.FirstAddress.ColumnNumber, range.RangeAddress.LastAddress.ColumnNumber)).ToHashSet();
    for (int item = 0; item < count; item++)
    {
        int row = item + 2;
        if (s.Cell(row, 1).DataType != XLDataType.Text || s.Cell(row, 1).GetString() != "item-" + item) throw new Exception($"Value mismatch at A{row}");
        for (int column = 2; column <= columns; column++)
            if (s.Cell(row, column).DataType != XLDataType.Number || s.Cell(row, column).GetDouble() != item + column - 1) throw new Exception($"Numeric value mismatch at row {row}, column {column}");
        if (s.Row(row).Height != 22 || s.Cell(row, 1).Style.Fill.BackgroundColor != XLColor.LightBlue) throw new Exception($"Style mismatch at row {row}");
        if ((mode == "merged" || mode == "merged-contained") && !merges.Contains((row, row, columns + 1, columns + 2))) throw new Exception($"Merge mismatch at row {row}");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "verified", mode, count }));
}
catch (MappingException ex)
{
    sw.Stop();
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "rejected", mode, count, elapsedMs = sw.Elapsed.TotalMilliseconds, outputBytes = output.Length, message = ex.Message }));
    bool expected = mode == "formula" ? ex.Message.Contains("Formulas inside repeated") : count >= 100000 && ex.Message.Contains("MaxOutputRows");
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
    "sourceHashes": {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(project.parent.glob("*.cs"))},
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
        + escape(str(project), {'"': '&quot;'}) + '" /></ItemGroup></Project>')
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

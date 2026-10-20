using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;

var specification = File.ReadAllBytes(args[0]);
var cases = JsonSerializer.Deserialize<Case[]>(specification)!;
var assembly = typeof(ReportCell).Assembly;
var core = Assembly.Load("ExcelRenderer.Core");
var observer = core.GetType("ExcelRenderer.Core.Rendering.ConversionMetrics")!.GetProperty("Observer", BindingFlags.Static | BindingFlags.NonPublic)!;
var hashes = new Dictionary<string, string>
{
    ["input"] = Convert.ToHexString(SHA256.HashData(specification)),
    ["main"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))),
    ["core"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core.Location))),
};
Console.WriteLine(JsonSerializer.Serialize(new { environment = new { runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), hashes } }));
foreach (var item in cases)
{
    var side = new BorderSide(item.Double ? 0.75 : 0.5, LineStyle: Enum.Parse<BorderLineStyle>(item.LineStyle ?? (item.Double ? "Double" : "Solid")));
    var border = new BorderStyle(side, side, side, side);
    var cells = new Dictionary<CellAddress, ReportCell>();
    for (var row = 1; row <= item.Rows; row += item.Merged ? 2 : 1)
    {
        for (var column = 1; column <= item.Columns; column++)
        {
            var address = new CellAddress(row, column);
            cells.Add(address, item.Merged
                ? new(null, CellStyle.Default, RowSpan: 2)
                {
                    MergedBorders = [new(address, new(side, side, side)), new(new(row + 1, column), new(side, Right: side, Bottom: side))],
                }
                : new(null, CellStyle.Default with { Border = item.Borders ? border : null }));
        }
    }

    var sheet = new ReportSheet(item.Name, cells, new Dictionary<int, ColumnDefinition>(), new Dictionary<int, RowDefinition>(), [], new(1500, 1000, 10, 10, 10, 10));
    for (var round = -3; round < 5; round++)
    {
        var counters = new Dictionary<string, double>();
        observer.SetValue(null, round == -3 ? (Action<string, double>)((name, value) => counters[name] = counters.GetValueOrDefault(name) + value) : null);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocated = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        long bytes = 0;
        long commandCount = 0;
        var measurer = new EmptyMeasurer();
        var generator = new DrawCommandGeneratorPass();
        var renderer = new SvgRenderer();
        if (item.Continuous)
        {
            var type = assembly.GetType("ExcelRenderer.Layout.ContinuousLayoutPlan")!;
            var plan = Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null, [sheet, measurer], null)!;
            var commands = (IEnumerable<DrawCommand>)typeof(DrawCommandGeneratorPass).GetMethod("GenerateContinuous", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(generator, [plan, measurer])!;
            var width = (double)type.GetProperty("Width", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plan)!;
            var height = (double)type.GetProperty("Height", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(plan)!;
            using var output = new MemoryStream();
            renderer.RenderCanvas(Count(commands), width, height, output);
            bytes = output.Length;
        }
        else
        {
            var document = new ReportLayoutEngine(measurer).Layout(sheet);
            foreach (var page in document.Pages)
            {
                using var output = new MemoryStream();
                renderer.RenderPage(Count(generator.Generate(new([page]))), sheet.PageSettings, output);
                bytes += output.Length;
            }
        }

        timer.Stop();
        var allocationBytes = GC.GetTotalAllocatedBytes(true) - allocated;
        observer.SetValue(null, null);
        Console.WriteLine(JsonSerializer.Serialize(new { name = item.Name, round, warmup = round < 0, diagnostic = round == -3, milliseconds = timer.Elapsed.TotalMilliseconds, allocationBytes, peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64, commandCount, outputBytes = bytes, counters }));

        IEnumerable<DrawCommand> Count(IEnumerable<DrawCommand> commands)
        {
            foreach (var command in commands)
            {
                commandCount++;
                yield return command;
            }
        }
    }
}

record Case(string Name, int Rows, int Columns, bool Merged, bool Continuous, bool Double, string? LineStyle = null, bool Borders = true);

sealed class EmptyMeasurer : ITextMeasurer
{
    public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => throw new InvalidOperationException("Benchmark cells must not contain text.");
}

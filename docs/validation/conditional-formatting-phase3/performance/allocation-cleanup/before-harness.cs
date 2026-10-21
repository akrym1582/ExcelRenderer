
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
for (var iteration = 0; iteration < 13; iteration++) {
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
        using var prepared = await ExcelRenderer.OpenXml.Reader.Input.WorkbookInputPreparer.ReadAsync(input, new(), default);
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
    Console.WriteLine(JsonSerializer.Serialize(new { iteration, warmup = iteration < 3,
        elapsedMs = timer.Elapsed.TotalMilliseconds, allocatedBytes = allocation,
        peakRssBytes = process.PeakWorkingSet64, count, painted, metrics }));
    ConversionMetrics.Observer = null;
    snapshotObserver.SetValue(null, null);
}

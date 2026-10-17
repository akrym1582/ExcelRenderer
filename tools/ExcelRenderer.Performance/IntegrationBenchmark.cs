using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Mapping;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Diagnostics;
using ExcelRenderer.Rendering;
using S = DocumentFormat.OpenXml.Spreadsheet;

/// <summary>Generates integration inputs and measures one operation in an isolated process.</summary>
internal static class IntegrationBenchmark
{
    /// <summary>Runs a fixture generator or a single timed worker.</summary>
    /// <param name="args">Command, path, operation, backend, and output-row count.</param>
    /// <returns>The completed operation.</returns>
    internal static async Task RunAsync(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        if (args[0] == "integration-generate")
        {
            Generate(args[1], int.Parse(args[2], CultureInfo.InvariantCulture));
            return;
        }

        var path = args[1];
        var operation = args[2];
        var backend = args[3];
        var count = int.Parse(args[4], CultureInfo.InvariantCulture);
        if (backend is not ("openxml" or "auto"))
        {
            throw new ArgumentException("The available backends are openxml and auto.", nameof(args));
        }

        var data = Data(path.Contains("nested", StringComparison.Ordinal), count);
        var metrics = new Dictionary<string, double>();
        var io = new Dictionary<string, long>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        OperationMetrics.Observer = (name, value) => io[name] = io.GetValueOrDefault(name) + value;
        var readerBackend = backend == "openxml" ? ReaderBackend.OpenXml : ReaderBackend.Auto;
        var mappingBackend = backend == "openxml" ? MappingBackend.OpenXml : MappingBackend.Auto;
        ReaderBackendSelection? readerSelection = null;
        MappingBackendSelection? mappingSelection = null;
        var fonts = new FontOptions { AllowSystemFonts = false, FontFiles = [Path.GetFullPath("third_party/NotoSansJP/NotoSansJP-Regular.ttf")] };
        var reads = new ExcelReadOptions { Backend = readerBackend, FormulaDisplayMode = FormulaDisplayMode.CachedValue, BackendSelected = selection => readerSelection = selection };
        var maps = new MappingOptions
        {
            Backend = mappingBackend,
            MaxOutputRows = 1048576,
            BackendSelected = selection => mappingSelection = selection,
            Input = new() { AllowTemporaryFiles = true },
        };
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var input = File.OpenRead(path);
        using var mapped = new MemoryStream();
        using var output = new MemoryStream();
        var allocated = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        var cells = 0;
        long checksum = 0;
        try
        {
            if (operation == "formula-preflight")
            {
                if (WorkbookFormulaInspection.Find(input, CancellationToken.None) is not null)
                {
                    throw new InvalidOperationException("The benchmark input contains a formula.");
                }
            }

            if (operation is "mapping" or "mapping-render")
            {
                ExcelTemplateMapper.Map(input, mapped, data, maps);
                mapped.Position = 0;
            }

            Stream source = operation == "mapping-render" ? mapped : input;
            if (operation == "reader")
            {
                var document = new ExcelReader(new FontManager(fonts)).Read(source, reads);
                cells = document.Sheets.Sum(sheet => sheet.Cells.Count);
                checksum = document.Sheets.SelectMany(sheet => sheet.Cells.Values).Sum(cell => (long)(cell.Text?.Length ?? 0));
            }
            else if (operation is "render" or "mapping-render")
            {
                await ExcelConverter.RenderAsync(source, new RenderRequest { ReadOptions = reads, OutputFormat = OutputFormat.Pdf, FontOptions = fonts, Input = new() { AllowTemporaryFiles = true } }, new SingleStreamOutputSink(output));
                checksum = output.Length;
            }

            timer.Stop();
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Operation = operation,
                RequestedBackend = backend,
                Rows = count,
                ElapsedMs = timer.Elapsed.TotalMilliseconds,
                AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated,
                PeakRssBytes = process.PeakWorkingSet64,
                Cells = cells == 0 ? (int)metrics.GetValueOrDefault("model.cells") : cells,
                Checksum = checksum,
                MappingOutputBytes = mapped.Length,
                ReaderSelection = readerSelection,
                MappingSelection = mappingSelection,
                Metrics = metrics,
                IoMetrics = io,
            }));
            if (args.Length > 5 && operation == "mapping")
            {
                File.WriteAllBytes(args[5], mapped.ToArray());
            }
        }
        finally
        {
            ConversionMetrics.Observer = null;
            OperationMetrics.Observer = null;
        }
    }

    private static object Data(bool nested, int count) => nested
        ? new { groups = Enumerable.Range(0, 100).Select(index => new { id = index, items = Enumerable.Range(1, Math.Max(0, (count / 100) - 1)).ToArray() }).ToArray() }
        : new { items = Enumerable.Range(1, count).ToArray() };

    private static void Generate(string directory, int count)
    {
        Directory.CreateDirectory(directory);
        foreach (var kind in new[] { "plain", "nested", "geometry" })
        {
            using (var package = SpreadsheetDocument.Create(Path.Combine(directory, $"{kind}-{count}-template.xlsx"), SpreadsheetDocumentType.Workbook))
            {
                var workbook = package.AddWorkbookPart();
                workbook.Workbook = new S.Workbook(new S.Sheets());
                workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = FixtureWorkbook.Styles();
                var values = kind == "nested" ? new[] { "Header", "**@start-array groups[*] as group", "**@group.id", "**@start-array @group.items[*] as item", "**@item", "**@end-array", "**@end-array", "Footer" } : new[] { "Header", "**items[*]", "Footer" };
                var data = new S.SheetData(values.Select((value, index) => new S.Row(FixtureWorkbook.Text(index + 1, 1, value)) { RowIndex = (uint)index + 1 }));
                var repeated = kind == "nested" ? 5 : 2;
                data.Elements<S.Row>().ElementAt(repeated - 1).Append(new S.Cell { CellReference = "B" + repeated, StyleIndex = 1 });
                var worksheet = new S.Worksheet(data);
                if (kind == "geometry")
                {
                    data.Elements<S.Row>().ElementAt(1).Append(FixtureWorkbook.Text(2, 3, "merged"));
                    worksheet.Append(new S.MergeCells(new S.MergeCell { Reference = "C2:D2" }));
                    workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("'report'!$A$1:$D$3") { Name = "_xlnm.Print_Area", LocalSheetId = 0 }, new S.DefinedName("'report'!$1:$1") { Name = "_xlnm.Print_Titles", LocalSheetId = 0 });
                }

                FixtureWorkbook.Sheet(workbook, "report", worksheet);
            }

            var path = Path.Combine(directory, $"{kind}-{count}-template.xlsx");
            ExcelTemplateMapper.Map(path, Path.Combine(directory, $"{kind}-{count}-values.xlsx"), Data(kind == "nested", count), new MappingOptions
            {
                Backend = MappingBackend.OpenXml,
                MaxOutputRows = 1048576,
                Input = new() { AllowTemporaryFiles = true },
            });
        }
    }
}

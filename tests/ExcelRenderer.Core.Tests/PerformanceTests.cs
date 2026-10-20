using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer.Core.Rendering;
using Xunit;
using Xunit.Abstractions;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class PerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100, false)]
    [InlineData(100, true)]
    [InlineData(1000, false)]
    [InlineData(1000, true)]
    [InlineData(5000, false)]
    [InlineData(5000, true)]
    [InlineData(10000, false)]
    [InlineData(10000, true)]
    [Trait("Category", "Performance")]
    public async Task Conversion_measurements_exclude_fixture_creation_and_pdf_validation(int rows, bool images)
    {
        using var workbook = TestSupport.Workbook(part =>
        {
            var sheet = TestSupport.Sheet(part);
            var data = sheet.GetFirstChild<S.SheetData>()!;
            data.RemoveAllChildren();
            var breaks = new S.RowBreaks();
            for (var row = 1; row <= rows; row++)
            {
                data.Append(new S.Row(TestWorkbook.Text("A" + row, $"row-{row:D5}"), TestWorkbook.Text("B" + row, "value"))
                { RowIndex = (uint)row, Height = 12, CustomHeight = true });
                if (row % 50 == 0 && row < rows) breaks.Append(new S.Break { Id = (uint)row, ManualPageBreak = true });
                if (images && row % 50 == 1) TestWorkbook.Image(part.WorksheetParts.Single(), TestSupport.Png(), "image-" + row, row - 1, 1);
            }

            sheet.Append(new S.PageMargins { Left = 0.5, Right = 0.5, Top = 0.5, Bottom = 0.5, Header = 0, Footer = 0 },
                new S.PageSetup { PaperSize = 1, Scale = 100 }, breaks);
            TestSupport.Print(part, $"First!$A$1:$B${rows}");
        });
        var inputBytes = workbook.ToArray();
        var warmup = await Measure(inputBytes, rows, images);
        var trials = new List<Sample>();
        for (var trial = 0; trial < 3; trial++) trials.Add(await Measure(inputBytes, rows, images));
        var report = new
        {
            Rows = rows,
            Images = images,
            Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ObserverEnabled = true,
            ProcessorCount = Environment.ProcessorCount,
            ServerGC = System.Runtime.GCSettings.IsServerGC,
            InputSha256 = Convert.ToHexString(SHA256.HashData(inputBytes)),
            CoreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(ExcelConverter).Assembly.Location))),
            FontSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(TestSupport.JapaneseFont))),
            Warmup = warmup,
            Trials = trials,
            MedianMilliseconds = trials.OrderBy(sample => sample.Milliseconds).ElementAt(1).Milliseconds,
        };
        var directory = Path.Combine(AppContext.BaseDirectory, "SampleOutputs", "Performance");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(directory, $"{rows}-{(images ? "images" : "text")}.json"), json);
        await File.WriteAllBytesAsync(Path.Combine(directory, $"{rows}-{(images ? "images" : "text")}.xlsx"), inputBytes);
        output.WriteLine(json);
    }

    private static async Task<Sample> Measure(byte[] bytes, int rows, bool images)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var pdf = new MemoryStream();
        var metrics = new Dictionary<string, double>();
        ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
        ConversionResult result;
        long allocated;
        double elapsed;
        var managedBefore = GC.GetTotalMemory(false);
        using var process = Process.GetCurrentProcess();
        var workingSetBefore = process.WorkingSet64;
        try
        {
            var before = GC.GetTotalAllocatedBytes(precise: true);
            var started = Stopwatch.GetTimestamp();
            result = await ExcelConverter.ConvertAsync(input, pdf, TestSupport.Options);
            elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        }
        finally { ConversionMetrics.Observer = null; }

        var managedAfter = GC.GetTotalMemory(false);
        process.Refresh();
        var workingSetAfter = process.WorkingSet64;
        var handlesAfter = process.HandleCount;
        // Both counters stop before PDF parsing, extraction, assertions and output serialization.
        using var document = TestSupport.Open(pdf.ToArray());
        var expectedPages = rows / 50;
        Assert.Equal(expectedPages, result.PageCount);
        Assert.Equal(expectedPages, document.NumberOfPages);
        for (var index = 1; index <= expectedPages; index++)
        {
            var page = document.GetPage(index);
            var labels = Enumerable.Range((index - 1) * 50 + 1, 50).Select(row => $"row-{row:D5}");
            Assert.Equal(string.Concat(labels), page.Text.Replace("value", string.Empty));
            Assert.Equal(images ? 1 : 0, page.NumberOfImages);
        }

        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("SavedPageBreaks", diagnostic.Code));
        Assert.Equal(1, metrics["pdfSave"]);
        Assert.Equal(2 * expectedPages, metrics["corePageBuild"]);
        Assert.Equal(2 * expectedPages, metrics["coreCommandBuild"]);
        Assert.Equal(metrics["typefaceCreated"], metrics["typefaceDisposed"]);
        Assert.Equal(metrics["pdfFontCreated"], metrics["pdfFontDisposed"]);
        Assert.True(allocated > 0);
        Assert.True(elapsed > 0);
        return new(elapsed, allocated, managedBefore, managedAfter, workingSetBefore, workingSetAfter, handlesAfter, pdf.Length, result.PageCount);
    }

    private sealed record Sample(double Milliseconds, long AllocatedBytes, long ManagedBefore, long ManagedAfter,
        long WorkingSetBefore, long WorkingSetAfter, int HandlesAfter, long PdfBytes, int Pages);
}

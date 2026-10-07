using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using ClosedXML.Excel;
using ExcelRenderer;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

var command = args[0];
var path = args[1];
if (command == "generate")
{
    var rows = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
    var mode = args.Length > 3 ? args[3] : "text";
    using var workbook = new XLWorkbook();
    var sheet = workbook.AddWorksheet("Synthetic");
    var range = sheet.Range(1, 1, rows, 20);
    range.Style.Font.FontName = "Noto Sans JP";
    range.Style.Font.FontSize = 10;
    range.Style.Fill.BackgroundColor = XLColor.LightGray;
    range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    range.Style.Border.OutsideBorder = XLBorderStyleValues.Double;
    sheet.Columns(1, 20).Width = 10;
    sheet.Rows(1, rows).Height = 18;
    if (mode != "empty")
    {
        for (var row = 1; row <= rows; row++)
        {
            for (var column = 1; column <= 20; column++)
            {
                sheet.Cell(row, column).Value = mode == "latin" ? "Report ABC 123" : "帳票 ABC 123";
            }
        }
    }

    if (mode == "bold")
    {
        range.Style.Font.Bold = true;
    }

    if (mode == "rowstyle")
    {
        sheet.Row(rows + 10000).Style.Fill.BackgroundColor = XLColor.Yellow;
        sheet.Column(30).Style.Fill.BackgroundColor = XLColor.LightBlue;
    }

    if (mode == "merges")
    {
        for (var row = 1; row <= rows; row += 2)
        {
            sheet.Range(row, 1, Math.Min(row + 1, rows), 2).Merge();
        }

        sheet.PageSetup.SetRowsToRepeatAtTop(1, 2);
    }

    if (mode == "far")
    {
        sheet.Cell(rows + 10000, 30).Style.Fill.BackgroundColor = XLColor.Red;
        sheet.Row(rows + 20000).Height = 25;
    }

    if (mode == "wrap")
    {
        range.Style.Alignment.WrapText = true;
        sheet.Cell(1, 1).Value = string.Concat(Enumerable.Repeat("折り返し ABC e\u0301 葛\U000E0100 ", 30));
    }

    sheet.PageSetup.PrintAreas.Add(range.RangeAddress.ToString());
    if (mode == "mixed")
    {
        sheet.PageSetup.SetRowsToRepeatAtTop(1, 2);
        sheet.PageSetup.PrintAreas.Add("A1:D10");
        sheet.Cell(1, 1).SetHyperlink(new XLHyperlink("#Other!A1"));
        var other = workbook.AddWorksheet("Other");
        other.Cell("A1").Value = "リンク先";
        other.Style.Font.FontName = "Noto Sans JP";
        other.PageSetup.PaperSize = XLPaperSize.A3Paper;
        using var bitmap = new SkiaSharp.SKBitmap(20, 20);
        bitmap.Erase(SkiaSharp.SKColors.Blue);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var imageStream = new MemoryStream(data.ToArray());
        sheet.AddPicture(imageStream).MoveTo(sheet.Cell(35, 1)).WithSize(100, 250);
    }

    workbook.SaveAs(path);
    return;
}

var repeats = args.Length > 2 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 1;
var fontPath = Path.GetFullPath(args.Length > 3 ? args[3] : "third_party/NotoSansJP/NotoSansJP-Regular.ttf");
var bytes = await File.ReadAllBytesAsync(path);
using var zipStream = new MemoryStream(bytes, false);
using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
var entries = zip.Entries.Select(entry =>
{
    using var source = entry.Open();
    var xml = entry.FullName.EndsWith(".xml", StringComparison.Ordinal) ? XDocument.Load(source) : null;
    return new
    {
        entry.FullName,
        entry.CompressedLength,
        entry.Length,
        Cells = xml?.Descendants().Count(element => element.Name.LocalName == "c"),
        Rows = xml?.Descendants().Count(element => element.Name.LocalName == "row"),
        Columns = xml?.Descendants().Count(element => element.Name.LocalName == "col"),
        Styled = xml?.Descendants().Count(element => element.Attribute("s") is not null),
        FormatRecords = xml?.Descendants().Count(element => element.Name.LocalName == "xf"),
        PrintRanges = xml?.Descendants().Where(element => element.Name.LocalName == "definedName").Select(element => element.Value).ToArray(),
    };
}).ToArray();
for (var iteration = 0; iteration < repeats; iteration++)
{
    var phases = new Dictionary<string, double>();
    var snapshots = new List<object>();
    ConversionMetrics.Observer = (name, value) =>
    {
        phases[name] = phases.GetValueOrDefault(name) + value;
        if (name.EndsWith(".ms", StringComparison.Ordinal))
        {
            using var snapshotProcess = Process.GetCurrentProcess();
            var snapshot = new { Phase = name, DurationMs = value, ManagedBytes = GC.GetTotalMemory(false), snapshotProcess.WorkingSet64, snapshotProcess.PrivateMemorySize64, AllocatedBytes = GC.GetTotalAllocatedBytes(false) };
            snapshots.Add(snapshot);
            Console.Error.WriteLine(JsonSerializer.Serialize(snapshot));
        }
    };
    using var input = new MemoryStream(bytes, false);
    using var output = File.Create(path + ".pdf");
    using var process = Process.GetCurrentProcess();
    var allocated = GC.GetTotalAllocatedBytes(true);
    var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
    var timer = Stopwatch.StartNew();
    var result = await ExcelConverter.RenderAsync(
        input,
        new()
        {
            OutputFormat = OutputFormat.Pdf,
            FontOptions = new()
            {
                AllowSystemFonts = false,
                UseFontPack = false,
                Registrations = [new FontRegistration("Noto Sans JP", fontPath)],
                FallbackFamilies = ["Noto Sans JP"],
            },
        },
        new SingleStreamOutputSink(output));
    timer.Stop();
    process.Refresh();
    var conversionAllocated = GC.GetTotalAllocatedBytes(true) - allocated;
    var conversionManaged = GC.GetTotalMemory(false);
    var conversionCollections = Enumerable.Range(0, 3).Select(index => GC.CollectionCount(index) - collections[index]).ToArray();
    var heap = GC.GetGCMemoryInfo();
    output.Flush();
    output.Position = 0;
    using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
    var objects = pdf.Internals.GetAllObjects().OfType<PdfDictionary>().ToArray();
    var fontStreams = objects.SelectMany(dictionary => new[] { "/FontFile", "/FontFile2", "/FontFile3" }
        .Select(key => dictionary.Elements.GetDictionary(key)))
        .Where(dictionary => dictionary is not null).Distinct().ToArray();
    long? postCollectionManaged = null;
    long? postCollectionWorkingSet = null;
    long? postCollectionPrivateBytes = null;
    if (repeats > 1)
    {
        // Harness-only retention check; collection is outside conversion timing.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        postCollectionManaged = GC.GetTotalMemory(false);
        process.Refresh();
        postCollectionWorkingSet = process.WorkingSet64;
        postCollectionPrivateBytes = process.PrivateMemorySize64;
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Iteration = iteration,
        OS = RuntimeInformation.OSDescription,
        Runtime = RuntimeInformation.FrameworkDescription,
        CPU = Environment.ProcessorCount,
        InputSHA256 = Convert.ToHexString(SHA256.HashData(bytes)),
        FontSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fontPath))),
        AllowSystemFonts = false,
        InputBytes = bytes.Length,
        TotalMs = timer.Elapsed.TotalMilliseconds,
        AllocatedBytes = conversionAllocated,
        process.PeakWorkingSet64,
        process.WorkingSet64,
        process.PrivateMemorySize64,
        ManagedBytes = conversionManaged,
        PostCollectionManagedBytes = postCollectionManaged,
        PostCollectionWorkingSet64 = postCollectionWorkingSet,
        PostCollectionPrivateBytes = postCollectionPrivateBytes,
        GC = conversionCollections,
        Heap = heap.GenerationInfo.ToArray().Select(info => new { info.SizeAfterBytes, info.FragmentationAfterBytes }),
        FontObjects = objects.Count(dictionary => dictionary.Elements.GetName("/Type") == "/Font"),
        ImageObjects = objects.Count(dictionary => dictionary.Elements.GetName("/Subtype") == "/Image"),
        FontStreams = fontStreams.Length,
        FontStreamBytes = fontStreams.Sum(dictionary => dictionary?.Stream?.Value.Length ?? 0),
        Pages = result.Pages.Count,
        OutputBytes = output.Length,
        Phases = phases,
        Snapshots = snapshots,
        Zip = entries,
    }));
    ConversionMetrics.Observer = null;
}

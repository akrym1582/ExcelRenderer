using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using PdfSharp.Pdf.IO;
using SkiaSharp;
using SlimValidation;
#if REFERENCE
using ExcelRenderer;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
#else
using ExcelRenderer.Slim;
#endif

if (args[0] == "generate")
{
    using var workbook = new XLWorkbook();
    var sheet = workbook.AddWorksheet("Report");
    var rows = int.Parse(args[2], CultureInfo.InvariantCulture);
    workbook.Style.Font.FontName = "Noto Sans JP";
    sheet.Style.Font.FontName = "Noto Sans JP";
    sheet.Columns(1, 2).Width = 16;
    sheet.Rows(1, rows).Height = 18;
    for (var row = 1; row <= rows; row++)
    {
        for (var column = 1; column <= 2; column++)
        {
            sheet.Cell(row, column).Value = $"帳票 ABC {row}";
        }
    }

    if (args.Length > 3 && args[3] == "images")
    {
        using var bitmap = new SKBitmap(64, 24);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        for (var row = 1; row <= rows; row += 10)
        {
            using var stream = bytes.AsStream();
            sheet.AddPicture(stream).MoveTo(sheet.Cell(row, 2));
        }
    }

    sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
    sheet.PageSetup.PrintAreas.Add(sheet.Range(1, 1, rows, 2).RangeAddress.ToString());
    workbook.SaveAs(args[1]);
    return;
}

if (args[0] == "normalize")
{
    using (var workbook = new XLWorkbook(args[1]))
    {
        workbook.Style.Font.FontName = "Noto Sans JP";
        foreach (var sheet in workbook.Worksheets)
        {
            sheet.Style.Font.FontName = "Noto Sans JP";
            sheet.Style.Font.Bold = false;
            sheet.Style.Font.Italic = false;
            foreach (var cell in sheet.CellsUsed(XLCellsUsedOptions.All))
            {
                cell.Style.Font.FontName = "Noto Sans JP";
                cell.Style.Font.Bold = false;
                cell.Style.Font.Italic = false;
                cell.Style.Alignment.TextRotation = 0;
            }
        }

        workbook.SaveAs(args[2]);
    }

    using var package = SpreadsheetDocument.Open(args[2], true);
    foreach (var part in package.WorkbookPart!.WorksheetParts)
    {
        part.Worksheet.RemoveAllChildren<DocumentFormat.OpenXml.Spreadsheet.Hyperlinks>();
        part.Worksheet.Save();
        if (part.DrawingsPart?.WorksheetDrawing is { } drawing)
        {
            foreach (var anchor in drawing.ChildElements.ToArray())
            {
                if (!anchor.Elements<DocumentFormat.OpenXml.Drawing.Spreadsheet.Picture>().Any())
                {
                    anchor.Remove();
                    continue;
                }

                foreach (var picture in anchor.Elements<DocumentFormat.OpenXml.Drawing.Spreadsheet.Picture>())
                {
                    picture.BlipFill?.SourceRectangle?.Remove();
                    if (picture.ShapeProperties?.Transform2D is { } transform)
                    {
                        transform.Rotation = 0;
                        transform.HorizontalFlip = false;
                        transform.VerticalFlip = false;
                    }
                }
            }

            drawing.Save();
        }
    }

    return;
}

var repeats = args.Length > 4 ? int.Parse(args[4], CultureInfo.InvariantCulture) : 1;
var samples = new List<object>();
for (var iteration = 0; iteration < repeats; iteration++)
{
    var maximumManaged = GC.GetTotalMemory(false);
    using var timer = new Timer(_ => InterlockedExtensions.UpdateMaximum(ref maximumManaged, GC.GetTotalMemory(false)), null, 0, 10);
    var clock = Stopwatch.StartNew();
#if REFERENCE
    File.Delete(args[2]);
    await ExcelConverter.ConvertToPdfAsync(args[1], args[2], new PdfExportOptions
    {
        Hyperlinks = HyperlinkMode.None,
        FontOptions = new FontOptions
        {
            FontFiles = [args[3]],
            AllowSystemFonts = false,
            UseFontPack = false,
            FallbackFamilies = ["Noto Sans JP"],
        },
    });
#else
    using (var input = File.OpenRead(args[1]))
    using (var output = new FileStream(args[2], FileMode.Create, FileAccess.ReadWrite))
    {
        await SlimExcelConverter.ConvertAsync(input, output, new() { FontFilePath = args[3] });
    }
#endif
    clock.Stop();
    using var process = Process.GetCurrentProcess();
    using var document = PdfReader.Open(args[2], PdfDocumentOpenMode.Import);
    samples.Add(new
    {
        Iteration = iteration,
        Milliseconds = clock.Elapsed.TotalMilliseconds,
        PeakRss = process.PeakWorkingSet64,
        Rss = process.WorkingSet64,
        ManagedMax = maximumManaged,
        ManagedAfter = GC.GetTotalMemory(false),
        Handles = process.HandleCount,
        Pages = document.PageCount,
        PdfBytes = new FileInfo(args[2]).Length,
    });
}

Console.WriteLine(JsonSerializer.Serialize(samples));

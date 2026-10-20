using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Core.Validation;
using SkiaSharp;
using UglyToad.PdfPig;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
#if REFERENCE
using ExcelRenderer;
using ExcelRenderer.Fonts;
using ExcelRenderer.Rendering;
#else
using ExcelRenderer.Core;
#endif

if (args[0] == "generate")
{
    using var document = SpreadsheetDocument.Create(args[1], SpreadsheetDocumentType.Workbook);
    var workbook = document.AddWorkbookPart();
    workbook.Workbook = new S.Workbook(new S.Sheets());
    var part = workbook.AddNewPart<WorksheetPart>();
    var rows = int.Parse(args[2], CultureInfo.InvariantCulture);
    var data = new S.SheetData();
    part.Worksheet = new S.Worksheet(new S.Columns(new S.Column { Min = 1, Max = 2, Width = 16.710625, CustomWidth = true }), data, new S.PageSetup { PaperSize = 9 });
    workbook.Workbook.Sheets!.Append(new S.Sheet { Id = workbook.GetIdOfPart(part), SheetId = 1, Name = "Report" });
    workbook.Workbook.DefinedNames = new S.DefinedNames(new S.DefinedName("'Report'!$A$1:$B$" + rows) { Name = "_xlnm.Print_Area", LocalSheetId = 0 });
    workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = new S.Stylesheet(
        new S.Fonts(new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Noto Sans JP" })),
        new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }), new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })),
        new S.Borders(new S.Border()),
        new S.CellStyleFormats(new S.CellFormat()),
        new S.CellFormats(new S.CellFormat()),
        new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }));
    for (var row = 1; row <= rows; row++)
    {
        data.Append(new S.Row(Enumerable.Range(1, 2).Select(column => new S.Cell(new S.InlineString(new S.Text($"帳票 ABC {row}")))
        {
            CellReference = (column == 1 ? "A" : "B") + row,
            DataType = S.CellValues.InlineString,
        })) { RowIndex = (uint)row, Height = 18, CustomHeight = true });
    }

    if (args.Length > 3 && args[3] == "images")
    {
        using var bitmap = new SKBitmap(64, 24);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
        for (var row = 1; row <= rows; row += 10)
        {
            AddFixtureImage(part, bytes.ToArray(), row - 1);
        }
    }

    return;
}

if (args[0] == "normalize")
{
    File.Copy(args[1], args[2], overwrite: true);
    using var package = SpreadsheetDocument.Open(args[2], true);
    var styles = package.WorkbookPart!.WorkbookStylesPart?.Stylesheet;
    foreach (var font in styles?.Fonts?.Elements<S.Font>() ?? [])
    {
        font.FontName = new S.FontName { Val = "Noto Sans JP" };
        font.Bold = null;
        font.Italic = null;
    }

    foreach (var format in styles?.CellFormats?.Elements<S.CellFormat>() ?? [])
    {
        if (format.Alignment is { } alignment)
        {
            alignment.TextRotation = 0;
        }
    }

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
    var allocated = GC.GetTotalAllocatedBytes(false);
    var clock = Stopwatch.StartNew();
#if REFERENCE
    File.Delete(args[2]);
    await ExcelConverter.ConvertToPdfAsync(args[1], args[2], new PdfExportOptions
    {
        Hyperlinks = HyperlinkMode.None,

        // Core measures column widths with the supplied font, including a saved Normal family that differs.
        ReadOptions = new()
        {
            ColumnWidthMetrics = new() { SourceFontFile = args[3] },
        },
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
        await ExcelConverter.ConvertAsync(input, output, new() { FontFilePath = args[3] });
    }
#endif
    clock.Stop();
    using var process = Process.GetCurrentProcess();
    using var document = PdfDocument.Open(args[2]);
    samples.Add(new
    {
        Iteration = iteration,
        Milliseconds = clock.Elapsed.TotalMilliseconds,
        AllocatedBytes = GC.GetTotalAllocatedBytes(false) - allocated,
        PeakRss = process.PeakWorkingSet64,
        Rss = process.WorkingSet64,
        ManagedMax = maximumManaged,
        ManagedAfter = GC.GetTotalMemory(false),
        Handles = process.HandleCount,
        Pages = document.NumberOfPages,
        PdfBytes = new FileInfo(args[2]).Length,
    });
}

Console.WriteLine(JsonSerializer.Serialize(samples));

static void AddFixtureImage(WorksheetPart worksheet, byte[] bytes, int row)
{
    var drawing = worksheet.DrawingsPart ?? worksheet.AddNewPart<DrawingsPart>();
    drawing.WorksheetDrawing ??= new Xdr.WorksheetDrawing();
    if (worksheet.Worksheet.GetFirstChild<S.Drawing>() is null)
    {
        worksheet.Worksheet.Append(new S.Drawing { Id = worksheet.GetIdOfPart(drawing) });
    }

    var image = drawing.AddImagePart(ImagePartType.Png);
    using (var source = new MemoryStream(bytes))
    {
        image.FeedData(source);
    }

    var picture = new Xdr.Picture(
        new Xdr.NonVisualPictureProperties(new Xdr.NonVisualDrawingProperties { Id = (uint)row + 1, Name = "image-" + row }, new Xdr.NonVisualPictureDrawingProperties()),
        new Xdr.BlipFill(new A.Blip { Embed = drawing.GetIdOfPart(image) }, new A.Stretch(new A.FillRectangle())),
        new Xdr.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 609600, Cy = 228600 }), new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));
    drawing.WorksheetDrawing.Append(new Xdr.OneCellAnchor(
        new Xdr.FromMarker(new Xdr.ColumnId("1"), new Xdr.ColumnOffset("0"), new Xdr.RowId(row.ToString(CultureInfo.InvariantCulture)), new Xdr.RowOffset("0")),
        new Xdr.Extent { Cx = 609600, Cy = 228600 },
        picture,
        new Xdr.ClientData()));
}

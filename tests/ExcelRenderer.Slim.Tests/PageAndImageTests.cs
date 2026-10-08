using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Slim.Drawing;
using ExcelRenderer.Slim.Excel;
using ExcelRenderer.Slim.Fonts;
using ExcelRenderer.Slim.Layout;
using ExcelRenderer.Slim.Model;
using ExcelRenderer.Slim.Pdf;
using ExcelRenderer.Slim.Rendering;
using PdfSharp.Pdf;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Slim.Tests;

public sealed class PageAndImageTests
{
    [Fact]
    public async Task All_sheets_include_hidden_and_empty_in_workbook_order()
    {
        var (result, bytes) = await TestSupport.Convert(workbook =>
        {
            workbook.Worksheet(1).PageSetup.PaperSize = XLPaperSize.A3Paper;
            var hidden = workbook.AddWorksheet("Hidden"); hidden.Cell(1, 1).Value = "hidden"; hidden.Hide(); hidden.PageSetup.PaperSize = XLPaperSize.LetterPaper;
            workbook.AddWorksheet("Empty").PageSetup.PaperSize = XLPaperSize.A4Paper;
        });
        Assert.Equal(3, result.PageCount); using var document = TestSupport.Open(bytes);
        TestSupport.Near(841.89, document.Pages[0].Width.Point); TestSupport.Near(612, document.Pages[1].Width.Point);
        TestSupport.Near(595.276, document.Pages[2].Width.Point);
        var selected = await TestSupport.Convert(workbook => workbook.AddWorksheet("Empty"), TestSupport.Options with { SheetName = "Empty" }); Assert.Equal(1, selected.Result.PageCount);
    }

    [Fact]
    public async Task Multiple_print_areas_are_paginated_independently()
    {
        var (result, _) = await TestSupport.Convert(workbook =>
        {
            var sheet = workbook.Worksheet(1); sheet.Cell(20, 3).Value = "second area";
            sheet.PageSetup.PrintAreas.Add("A1:B2"); sheet.PageSetup.PrintAreas.Add("C20:D21");
        }); Assert.Equal(2, result.PageCount);
    }

    [Fact]
    public async Task Empty_sheet_with_print_area_is_one_blank_page()
    {
        var (result, _) = await TestSupport.Convert(workbook =>
        {
            var sheet = workbook.Worksheet(1); sheet.Clear(); sheet.PageSetup.PrintAreas.Add("A1:B2");
        }); Assert.Equal(1, result.PageCount);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task Empty_multiple_print_areas_keep_the_existing_sheet_blank_page_rule(bool withHeader, int expected)
    {
        var (result, _) = await TestSupport.Convert(workbook =>
        {
            var sheet = workbook.Worksheet(1); sheet.Clear();
            sheet.PageSetup.PrintAreas.Add("A1:B2"); sheet.PageSetup.PrintAreas.Add("D4:E5");
            if (withHeader) sheet.PageSetup.Header.Center.AddText("header");
        });
        Assert.Equal(expected, result.PageCount);
    }

    [Fact]
    public async Task Manual_breaks_repeat_rows_landscape_and_fit_are_preserved()
    {
        var (result, bytes) = await TestSupport.Convert(workbook =>
        {
            var sheet = workbook.Worksheet(1); sheet.Cell(10, 1).Value = "last"; sheet.PageSetup.PrintAreas.Add("A1:B10");
            sheet.PageSetup.AddHorizontalPageBreak(5); sheet.PageSetup.SetRowsToRepeatAtTop(1, 1);
            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape; sheet.PageSetup.PaperSize = XLPaperSize.LetterPaper;
        });
        Assert.Equal(2, result.PageCount); using var document = TestSupport.Open(bytes);
        TestSupport.Near(792, document.Pages[0].Width.Point); TestSupport.Near(612, document.Pages[0].Height.Point);
        var (fit, _) = await TestSupport.Convert(workbook =>
        {
            var sheet = workbook.Worksheet(1); sheet.Cell(300, 1).Value = "last"; sheet.PageSetup.FitToPages(1, 1);
        }); Assert.Equal(1, fit.PageCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(49, 1)]
    [InlineData(50, 2)]
    [InlineData(75, 2)]
    [InlineData(100, 3)]
    [InlineData(-1, 0)]
    [InlineData(150, 0)]
    public void Image_is_drawn_only_on_page_containing_its_start(double start, int expectedPage)
    {
        var sheet = new ReportSheet("Image", new Dictionary<CellAddress, ReportCell>(),
            new Dictionary<int, ColumnDefinition> { [1] = new(50) },
            Enumerable.Range(1, 3).ToDictionary(i => i, _ => new RowDefinition(50)), [],
            new(100, 50, 0, 0, 0, 0, Scale: 1), new(new(1, 1), new(3, 1)),
            [new(new(1, 1), 0, start, 25, 80, TestSupport.Png())]);
        var measurer = new FakeMeasurer(); var plans = new ReportLayoutEngine(measurer).Plan(sheet); var plan = Assert.Single(plans);
        Assert.Equal(3, plan.Pages.Count);
        for (var i = 0; i < 3; i++)
        {
            var images = plan.Build(i, i + 1, 3, measurer).Images ?? [];
            Assert.Equal(expectedPage == i + 1 ? 1 : 0, images.Count);
            if (images.Count > 0) TestSupport.Near(80, images.Single().Bounds.Height);
        }
    }

    [Fact]
    public async Task Image_transforms_and_far_shapes_do_not_change_cell_image_output()
    {
        using var original = TestSupport.Workbook(workbook =>
        {
            var sheet = workbook.Worksheet(1);
            using var image = new MemoryStream(TestSupport.Png()); sheet.AddPicture(image, "logo").MoveTo(sheet.Cell(1, 1));
        });
        using (var package = SpreadsheetDocument.Open(original, true))
        {
            var drawing = package.WorkbookPart!.WorksheetParts.Single().DrawingsPart!.WorksheetDrawing!;
            var picture = drawing.Descendants<Xdr.Picture>().Single();
            picture.BlipFill!.SourceRectangle = new A.SourceRectangle { Left = 40000, Top = 20000 };
            picture.ShapeProperties!.Transform2D = new A.Transform2D { Rotation = 5400000, HorizontalFlip = true, VerticalFlip = true };
            drawing.Append(new Xdr.OneCellAnchor(new Xdr.FromMarker(new Xdr.ColumnId("1000"), new Xdr.ColumnOffset("0"), new Xdr.RowId("1000"), new Xdr.RowOffset("0")),
                new Xdr.Extent { Cx = 914400, Cy = 914400 },
                new Xdr.Shape(new Xdr.NonVisualShapeProperties(new Xdr.NonVisualDrawingProperties { Id = 50, Name = "far" }, new Xdr.NonVisualShapeDrawingProperties()),
                    new Xdr.ShapeProperties(new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle }),
                    new Xdr.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text("SHAPE TEXT"))))), new Xdr.ClientData()));
            drawing.Save();
        }
        original.Position = 0; using var output = new MemoryStream();
        var result = await SlimExcelConverter.ConvertAsync(original, output, TestSupport.Options);
        Assert.Equal(1, result.PageCount); Assert.DoesNotContain(result.Diagnostics, d => d.Code.Contains("Shape"));
        using var document = TestSupport.Open(output.ToArray());
        var images = document.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/XObject")!;
        Assert.Single(images.Elements.Keys);
    }

    [Fact]
    public async Task Hyperlink_cells_keep_text_without_PDF_annotations()
    {
        var (_, bytes) = await TestSupport.Convert(workbook =>
        {
            var sheet = workbook.Worksheet(1); sheet.Cell(1, 1).Value = "external";
            sheet.Cell(1, 1).SetHyperlink(new XLHyperlink("https://example.com"));
            sheet.Cell(2, 1).Value = "internal"; sheet.Cell(2, 1).SetHyperlink(new XLHyperlink("First!A1"));
        });
        using var document = TestSupport.Open(bytes); Assert.False(document.Pages[0].Elements.ContainsKey("/Annots"));
        Assert.Equal(2, PdfContentProbe.Read(bytes).Texts.Count);
    }

    [Fact]
    public async Task MDW_uses_snapshot_and_Normal_size()
    {
        using var input = TestSupport.Workbook(workbook => { workbook.Style.Font.FontSize = 20; workbook.Style.Font.FontName = "Missing"; workbook.Worksheet(1).Column(1).Width = 10; });
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var styles = package.WorkbookPart!.WorkbookStylesPart!.Stylesheet;
            var normal = styles.CellStyleFormats!.Elements<DocumentFormat.OpenXml.Spreadsheet.CellFormat>().First();
            styles.Fonts!.Elements<DocumentFormat.OpenXml.Spreadsheet.Font>().ElementAt((int)(normal.FontId?.Value ?? 0)).FontSize = new() { Val = 20 };
            styles.Save();
            var worksheet = package.WorkbookPart.WorksheetParts.Single().Worksheet;
            worksheet.GetFirstChild<DocumentFormat.OpenXml.Spreadsheet.Columns>()!.Elements<DocumentFormat.OpenXml.Spreadsheet.Column>().First().Width = 10;
            worksheet.Save();
        }
        input.Position = 0;
        await TestSupport.WithSheet(input, (sheet, font) =>
        {
            TestSupport.Near(ColumnWidthCalculator.ToPoints(10, font.MaximumDigitWidth(20)), sheet.Columns[1].Width);
        });
    }

    [Fact]
    public async Task DrawingML_anchors_preserve_EMU_and_preceding_dimensions()
    {
        var path = DrawingAnchorWorkbookFixture.Create();
        try
        {
            using var input = File.OpenRead(path);
            await TestSupport.WithSheet(input, (sheet, font) =>
            {
                var absolute = sheet.Images!.Single(i => i.Name == "absolute").DrawingAnchor!;
                TestSupport.Near(15, absolute.PositionX); TestSupport.Near(30, absolute.PositionY);
                TestSupport.Near(45, absolute.ExtentWidth); TestSupport.Near(60, absolute.ExtentHeight);
                var two = sheet.Images!.Single(i => i.Name == "two").DrawingAnchor!;
                Assert.Equal(new CellAddress(4, 3), two.From); Assert.Equal(new CellAddress(6, 5), two.To);
                TestSupport.Near(7.5, two.FromOffsetX); TestSupport.Near(7.5, two.ToOffsetY);
                var geometry = new SheetGeometry(sheet);
                var bounds = ObjectGeometry.GetSheetRect(geometry, new(4, 3), 0, 0, 0, 0, two);
                TestSupport.Near(sheet.Columns[1].Width + sheet.Columns[2].Width + 7.5, bounds.X);
                TestSupport.Near(9 + 10 + 11 + 15, bounds.Y);
            });
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Corrupt_workbook_picture_is_a_nonfatal_diagnostic()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            using var image = new MemoryStream(TestSupport.Png());
            workbook.Worksheet(1).AddPicture(image).MoveTo(workbook.Worksheet(1).Cell(1, 1));
        });
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var part = package.WorkbookPart!.WorksheetParts.Single().DrawingsPart!.ImageParts.Single();
            using var image = part.GetStream(FileMode.Create, FileAccess.Write); image.Write(new byte[] { 1, 2, 3 });
        }
        input.Position = 0; using var output = new MemoryStream();
        var result = await SlimExcelConverter.ConvertAsync(input, output, TestSupport.Options);
        Assert.Equal(1, result.PageCount); Assert.Contains(result.Diagnostics, d => d.Code == "ImageDecodeFailed");
    }

    [Fact]
    public void Corrupt_image_reports_diagnostic_and_next_page_still_renders()
    {
        var diagnostics = new List<ConversionDiagnostic>();
        using var images = new ImageResources(); using var document = new PdfDocument();
        var renderer = new PdfSharpRenderer { DiagnosticHandler = diagnostics.Add };
        renderer.AppendPage(document, new(100, 100), [new DrawImageCommand(1, new(0, 0, 10, 10), [1, 2, 3])]);
        renderer.AppendPage(document, new(100, 100), [new DrawImageCommand(2, new(0, 0, 10, 10), TestSupport.Png())]);
        Assert.Equal("ImageDecodeFailed", Assert.Single(diagnostics).Code);
        using var output = new MemoryStream(); document.Save(output, false); Assert.True(output.Length > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Page_order_has_independent_expected_cell_sequence(bool overThenDown)
    {
        var cells = new Dictionary<CellAddress, ReportCell>();
        for (var row = 1; row <= 2; row++)
            for (var column = 1; column <= 2; column++) cells[new(row, column)] = new($"{row},{column}", CellStyle.Default);
        var sheet = new ReportSheet("Sheet", cells,
            new Dictionary<int, ColumnDefinition> { [1] = new(50), [2] = new(50) },
            new Dictionary<int, RowDefinition> { [1] = new(50), [2] = new(50) }, [],
            new(50, 50, 0, 0, 0, 0, Scale: 1) { PageOrder = overThenDown ? PrintPageOrder.OverThenDown : PrintPageOrder.DownThenOver });
        var measurer = new FakeMeasurer(); var plan = new ReportLayoutEngine(measurer).Plan(sheet).Single();
        var actual = Enumerable.Range(0, plan.Pages.Count).Select(i => plan.Build(i, i + 1, 4, measurer).Cells.Single().Cell.Text).ToArray();
        Assert.Equal(overThenDown ? new[] { "1,1", "1,2", "2,1", "2,2" } : new[] { "1,1", "2,1", "1,2", "2,2" }, actual);
    }

    [Fact]
    public async Task Merged_cells_keep_only_top_left_text_and_double_border()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            var sheet = workbook.Worksheet(1); sheet.Range("A1:B2").Merge();
            sheet.Range("A1:B2").Style.Border.OutsideBorder = XLBorderStyleValues.Double;
        });
        await TestSupport.WithSheet(input, (sheet, _) =>
        {
            var cell = Assert.Single(sheet.Cells).Value;
            Assert.Equal(2, cell.RowSpan); Assert.Equal(2, cell.ColumnSpan);
            Assert.NotEmpty(cell.MergedBorders!);
            Assert.Contains(cell.MergedBorders!, border => border.Border.Left?.LineStyle == BorderLineStyle.Double);
        });
    }

    private sealed class FakeMeasurer : Abstractions.ITextMeasurer
    {
        public Abstractions.TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => new(0, 0);
    }
}

using ExcelRenderer.TestFixtures;
using S = DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Core.Drawing;
using ExcelRenderer.Core.Excel;
using ExcelRenderer.Core.Fonts;
using ExcelRenderer.Core.Layout;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Core.Pdf;
using ExcelRenderer.Core.Rendering;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using RenderingDiagnostic = ExcelRenderer.Core.Rendering.ConversionDiagnostic;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Core.Tests;

public sealed class PageAndImageTests
{
    [Fact]
    public async Task All_sheets_include_hidden_and_empty_in_workbook_order()
    {
        var (result, bytes) = await TestSupport.Convert(workbook =>
        {
            TestSupport.Sheet(workbook).Append(new S.PageSetup { PaperSize = 8 });
            SdkWorkbook.AddSheet(workbook, new S.Worksheet(new S.SheetData(new S.Row(SdkWorkbook.Text("A1", "hidden")) { RowIndex = 1 }), new S.PageSetup { PaperSize = 1 }), "Hidden", S.SheetStateValues.Hidden);
            SdkWorkbook.AddSheet(workbook, new S.Worksheet(new S.SheetData(), new S.PageSetup { PaperSize = 9 }), "Empty");
        });
        Assert.Equal(3, result.PageCount); using var document = TestSupport.Open(bytes);
        TestSupport.Near(841.89, document.Pages[0].Width.Point); TestSupport.Near(612, document.Pages[1].Width.Point);
        TestSupport.Near(595.276, document.Pages[2].Width.Point);
        var selected = await TestSupport.Convert(workbook => SdkWorkbook.AddSheet(workbook, new S.Worksheet(new S.SheetData()), "Empty"), TestSupport.Options with { SheetName = "Empty" }); Assert.Equal(1, selected.Result.PageCount);
    }

    [Fact]
    public async Task Multiple_print_areas_are_paginated_independently()
    {
        var (result, _) = await TestSupport.Convert(workbook =>
        {
            TestSupport.Sheet(workbook).GetFirstChild<S.SheetData>()!.Append(new S.Row(SdkWorkbook.Text("C20", "second area")) { RowIndex = 20 });
            TestSupport.Print(workbook, "First!$A$1:$B$2,First!$C$20:$D$21");
        }); Assert.Equal(2, result.PageCount);
    }

    [Fact]
    public async Task Empty_sheet_with_print_area_is_one_blank_page()
    {
        var (result, _) = await TestSupport.Convert(workbook =>
        {
            TestSupport.Sheet(workbook).GetFirstChild<S.SheetData>()!.RemoveAllChildren();
            TestSupport.Print(workbook, "First!$A$1:$B$2");
        }); Assert.Equal(1, result.PageCount);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task Empty_multiple_print_areas_keep_the_existing_sheet_blank_page_rule(bool withHeader, int expected)
    {
        var (result, _) = await TestSupport.Convert(workbook =>
        {
            var sheet = TestSupport.Sheet(workbook);
            sheet.GetFirstChild<S.SheetData>()!.RemoveAllChildren();
            TestSupport.Print(workbook, "First!$A$1:$B$2,First!$D$4:$E$5");
            if (withHeader) sheet.Append(new S.HeaderFooter(new S.OddHeader("&Cheader")));
        });
        Assert.Equal(expected, result.PageCount);
    }

    [Fact]
    public async Task Manual_breaks_repeat_rows_landscape_and_fit_are_preserved()
    {
        var (result, bytes) = await TestSupport.Convert(workbook =>
        {
            var sheet = TestSupport.Sheet(workbook);
            sheet.GetFirstChild<S.SheetData>()!.Append(new S.Row(SdkWorkbook.Text("A10", "last")) { RowIndex = 10 });
            TestSupport.Print(workbook, "First!$A$1:$B$10");
            TestSupport.Print(workbook, "First!$1:$1", "_xlnm.Print_Titles");
            sheet.Append(new S.PageSetup { PaperSize = 1, Orientation = S.OrientationValues.Landscape }, new S.RowBreaks(new S.Break { Id = 5, Min = 0, Max = 16383, ManualPageBreak = true }));
        });
        Assert.Equal(2, result.PageCount); using var document = TestSupport.Open(bytes);
        TestSupport.Near(792, document.Pages[0].Width.Point); TestSupport.Near(612, document.Pages[0].Height.Point);
        var (fit, _) = await TestSupport.Convert(workbook =>
        {
            var sheet = TestSupport.Sheet(workbook);
            sheet.InsertAt(new S.SheetProperties(new S.PageSetupProperties { FitToPage = true }), 0);
            sheet.GetFirstChild<S.SheetData>()!.Append(new S.Row(SdkWorkbook.Text("A300", "last")) { RowIndex = 300 });
            sheet.Append(new S.PageSetup { FitToWidth = 1, FitToHeight = 1 });
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
            SdkWorkbook.Image(workbook.WorksheetParts.Single(), TestSupport.Png(), "logo");
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
        var result = await ExcelConverter.ConvertAsync(original, output, TestSupport.Options);
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
            var part = workbook.WorksheetParts.Single();
            var data = part.Worksheet.GetFirstChild<S.SheetData>()!;
            data.Descendants<S.Text>().Single().Text = "external";
            data.Append(new S.Row(SdkWorkbook.Text("A2", "internal")) { RowIndex = 2 });
            var relationship = part.AddHyperlinkRelationship(new Uri("https://example.com"), true);
            part.Worksheet.Append(new S.Hyperlinks(new S.Hyperlink { Reference = "A1", Id = relationship.Id }, new S.Hyperlink { Reference = "A2", Location = "First!A1" }));
        });
        using var document = TestSupport.Open(bytes); Assert.False(document.Pages[0].Elements.ContainsKey("/Annots"));
        Assert.Equal(2, PdfContentProbe.Read(bytes).Texts.Select(text => text.Origin.Y).Distinct().Count());
    }

    [Fact]
    public async Task MDW_uses_snapshot_and_Normal_size()
    {
        using var input = TestSupport.Workbook(workbook =>
        {
            workbook.WorkbookStylesPart!.Stylesheet.Fonts!.GetFirstChild<S.Font>()!.FontSize = new S.FontSize { Val = 20 };
            TestSupport.Sheet(workbook).InsertAt(new S.Columns(new S.Column { Min = 1, Max = 1, Width = 10, CustomWidth = true }), 0);
        });
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
            SdkWorkbook.Image(workbook.WorksheetParts.Single(), TestSupport.Png());
        });
        using (var package = SpreadsheetDocument.Open(input, true))
        {
            var part = package.WorkbookPart!.WorksheetParts.Single().DrawingsPart!.ImageParts.Single();
            using var image = part.GetStream(FileMode.Create, FileAccess.Write); image.Write(new byte[] { 1, 2, 3 });
        }
        input.Position = 0; using var output = new MemoryStream();
        var result = await ExcelConverter.ConvertAsync(input, output, TestSupport.Options);
        Assert.Equal(1, result.PageCount); Assert.Contains(result.Diagnostics, d => d.Code == "ImageDecodeFailed");
    }

    [Fact]
    public void Corrupt_image_reports_diagnostic_and_next_page_still_renders()
    {
        var diagnostics = new List<RenderingDiagnostic>();
        using var images = new ImageResources();
        using var renderer = new CorePdfRenderer { DiagnosticHandler = diagnostics.Add }; renderer.Prepare();
        renderer.AppendPage( new(100, 100), [new DrawImageCommand(1, new(0, 0, 10, 10), [1, 2, 3])]);
        renderer.AppendPage( new(100, 100), [new DrawImageCommand(2, new(0, 0, 10, 10), TestSupport.Png())]);
        Assert.Equal("ImageDecodeFailed", Assert.Single(diagnostics).Code);
        using var output = new MemoryStream(); renderer.Complete(output); Assert.True(output.Length > 0);
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
            TestSupport.MergeDoubleBorder(workbook);
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

using ClosedXML.Excel;
using ReportEngine.Abstractions;
using ReportEngine.Drawing;
using ReportEngine.Excel;
using ReportEngine.Layout;
using ReportEngine.Model;
using ReportEngine.PdfSharp;
using SkiaSharp;
using Xunit;

namespace ReportEngine.Tests;

public sealed class LayoutPassTests
{
    [Fact]
    public void ColumnLayoutPass_assigns_cumulative_positions()
    {
        var context = CreateContext(columns: new Dictionary<int, ColumnDefinition>
        {
            [1] = new(80), [2] = new(120), [3] = new(60)
        });
        context.PrintArea = new(new(1, 1), new(1, 3));
        new HiddenRowColumnPass().Execute(context);

        new ColumnLayoutPass().Execute(context);

        Assert.Equal(0, context.ColumnLayouts[1].X);
        Assert.Equal(80, context.ColumnLayouts[2].X);
        Assert.Equal(200, context.ColumnLayouts[3].X);
    }

    [Fact]
    public void CellBoundsPass_uses_merged_cell_span()
    {
        var address = new CellAddress(1, 1);
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell> { [address] = new("title", CellStyle.Default, 2, 2) },
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(80), [2] = new(120) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(20), [2] = new(25) });
        context.PrintArea = new(address, new(2, 2));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);

        new CellBoundsPass().Execute(context);

        Assert.Equal(new ReportRect(0, 0, 200, 45), context.CellLayouts[address].Bounds);
    }

    [Fact]
    public void PaginationPass_splits_wide_sheets_at_column_boundaries()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("left", CellStyle.Default),
                [new(1, 2)] = new("right", CellStyle.Default)
            },
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(50), [2] = new(50) },
            pageSettings: new(70, 100, 10, 10, 10, 10));
        context.PrintArea = new(new(1, 1), new(1, 2));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        Assert.Equal(2, context.RenderDocument!.Pages.Count);
        Assert.Equal("left", Assert.Single(context.RenderDocument.Pages[0].Cells).Cell.Text);
        Assert.Equal("right", Assert.Single(context.RenderDocument.Pages[1].Cells).Cell.Text);
    }

    [Fact]
    public void PaginationPass_keeps_merged_cells_on_one_page()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("merged", CellStyle.Default, 2),
                [new(3, 1)] = new("next", CellStyle.Default)
            },
            rows: new Dictionary<int, RowDefinition> { [1] = new(30), [2] = new(30), [3] = new(30) },
            pageSettings: new(100, 80, 10, 10, 10, 10));
        context.PrintArea = new(new(1, 1), new(3, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        Assert.Equal(2, context.RenderDocument!.Pages.Count);
        Assert.Equal("merged", Assert.Single(context.RenderDocument.Pages[0].Cells).Cell.Text);
        Assert.Equal("next", Assert.Single(context.RenderDocument.Pages[1].Cells).Cell.Text);
    }

    [Fact]
    public void PaginationPass_does_not_split_rows()
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("one", CellStyle.Default),
            [new(2, 1)] = new("two", CellStyle.Default)
        };
        var context = CreateContext(cells: cells,
            rows: new Dictionary<int, RowDefinition> { [1] = new(40), [2] = new(40) },
            pageSettings: new(100, 90, 10, 10, 10, 10));
        context.PrintArea = new(new(1, 1), new(2, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        Assert.Equal(2, context.RenderDocument!.Pages.Count);
        Assert.All(context.RenderDocument.Pages, page => Assert.Single(page.Cells));
    }

    [Fact]
    public void PaginationPass_positions_images_from_their_anchor_cell()
    {
        var imageBytes = CreateImageBytes();
        var context = CreateContext(
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(80) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(20) },
            images: [new(new(1, 1), 2, 3, 10, 11, imageBytes)]);
        context.PrintArea = new(new(1, 1), new(1, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        var image = Assert.Single(context.RenderDocument!.Pages[0].Images!);
        Assert.Equal(new ReportRect(38, 39, 10, 11), image.Bounds);
        Assert.Equal(imageBytes, image.ImageBytes);
    }

    [Fact]
    public void ExcelReader_reads_worksheet_images()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        var imageBytes = CreateImageBytes();

        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Pictures.Add(new MemoryStream(imageBytes)).MoveTo(worksheet.Cell(2, 3), 4, 5);
                workbook.SaveAs(path);
            }

            var image = Assert.Single(new ExcelReader().Read(path).Sheets[0].Images!);

            Assert.Equal(new CellAddress(2, 3), image.Anchor);
            Assert.Equal(3, image.OffsetX);
            Assert.Equal(3.75, image.OffsetY);
            Assert.Equal(imageBytes, image.ImageBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExcelReader_reads_worksheet_header_and_footer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");

        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.PageSetup.Header.Left.AddText("ヘッダー");
                worksheet.PageSetup.Footer.Center.AddText("ページ &P / &N");
                workbook.SaveAs(path);
            }

            var headerFooter = new ExcelReader().Read(path).Sheets[0].HeaderFooter!;

            Assert.Equal("ヘッダー", headerFooter.Header.Left);
            Assert.Equal("ページ &P / &N", headerFooter.Footer.Center);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExcelReader_reads_print_settings_and_converts_column_widths()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Cell(2, 3).Value = "text";
                worksheet.PageSetup.PrintAreas.Add(2, 3, 4, 5);
                worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
                worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                worksheet.PageSetup.Margins.Left = 0.5;
                workbook.SaveAs(path);
            }

            var sheet = new ExcelReader().Read(path).Sheets[0];

            Assert.Equal(new CellRange(new(2, 3), new(4, 5)), sheet.PrintArea);
            Assert.Equal(841.89, sheet.PageSettings.Width, 2);
            Assert.Equal(36, sheet.PageSettings.MarginLeft, 2);
            Assert.Equal(48, sheet.Columns[3].Width, 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExcelReader_normalizes_empty_merged_cells_to_the_top_left_cell()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Range(1, 1, 2, 2).Merge();
                workbook.SaveAs(path);
            }

            var sheet = new ExcelReader().Read(path).Sheets[0];

            var cell = Assert.Single(sheet.Cells);
            Assert.Equal(new CellAddress(1, 1), cell.Key);
            Assert.Equal(2, cell.Value.RowSpan);
            Assert.Equal(2, cell.Value.ColumnSpan);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PaginationPass_adds_header_and_footer_texts_with_resolved_fields()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("one", CellStyle.Default),
                [new(2, 1)] = new("two", CellStyle.Default)
            },
            rows: new Dictionary<int, RowDefinition> { [1] = new(40), [2] = new(40) },
            pageSettings: new(100, 90, 10, 10, 10, 10),
            headerFooter: new(new("左 &A"), new("", "ページ &P / &N")));
        context.PrintArea = new(new(1, 1), new(2, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        var pages = context.RenderDocument!.Pages;
        Assert.Equal(2, pages.Count);
        Assert.Equal(["左 Sheet1", "ページ 1 / 2"], pages[0].HeaderFooterTexts!.Select(text => text.Text));
        Assert.Equal(["左 Sheet1", "ページ 2 / 2"], pages[1].HeaderFooterTexts!.Select(text => text.Text));
    }

    [Fact]
    public void PaginationPass_uses_first_and_even_page_headers()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("one", CellStyle.Default),
                [new(2, 1)] = new("two", CellStyle.Default)
            },
            rows: new Dictionary<int, RowDefinition> { [1] = new(40), [2] = new(40) },
            pageSettings: new(100, 90, 10, 10, 10, 10),
            headerFooter: new(new("通常"), new(), new("先頭"), EvenPageHeader: new("偶数")));
        context.PrintArea = new(new(1, 1), new(2, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        Assert.Equal("先頭", Assert.Single(context.RenderDocument!.Pages[0].HeaderFooterTexts!).Text);
        Assert.Equal("偶数", Assert.Single(context.RenderDocument.Pages[1].HeaderFooterTexts!).Text);
    }

    [Fact]
    public void PaginationPass_renders_header_and_footer_without_cells()
    {
        var context = CreateContext(cells: new Dictionary<CellAddress, ReportCell>(),
            headerFooter: new(new("ヘッダー"), new("フッター")));

        new PaginationPass().Execute(context);

        var page = Assert.Single(context.RenderDocument!.Pages);
        Assert.Equal(["ヘッダー", "フッター"], page.HeaderFooterTexts!.Select(text => text.Text));
    }

    [Fact]
    public void DrawCommandGenerator_orders_fill_before_border_before_text()
    {
        var style = CellStyle.Default with
        {
            Background = new(1, 2, 3),
            Border = new(new BorderSide())
        };
        var document = new RenderDocument(
        [
            new RenderPage(1, [new(new("text", style), new(0, 0, 10, 10))])
        ]);

        var commands = new DrawCommandGeneratorPass().Generate(document);

        Assert.Collection(commands,
            command => Assert.IsType<FillRectangleCommand>(command),
            command => Assert.IsType<DrawBorderCommand>(command),
            command => Assert.IsType<DrawTextCommand>(command));
    }

    [Fact]
    public void DrawCommandGenerator_adds_images_after_cell_content()
    {
        var imageBytes = CreateImageBytes();
        var document = new RenderDocument(
        [
            new RenderPage(1, [new(new("text", CellStyle.Default), new(0, 0, 10, 10))],
                [new(new(10, 20, 30, 40), imageBytes)])
        ]);

        var commands = new DrawCommandGeneratorPass().Generate(document);

        var image = Assert.IsType<DrawImageCommand>(commands.Last());
        Assert.Equal(new ReportRect(10, 20, 30, 40), image.Bounds);
        Assert.Equal(imageBytes, image.ImageBytes);
    }

    [Fact]
    public void PdfSharpRenderer_writes_a_pdf_document()
    {
        using var output = new MemoryStream();

        new PdfSharpRenderer().Render([], new PageSettings(), output);

        Assert.True(output.Length > 0);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(output.GetBuffer(), 0, 5));
    }

    [Fact]
    public void PdfSharpRenderer_renders_an_image()
    {
        using var output = new MemoryStream();
        var command = new DrawImageCommand(1, new(0, 0, 20, 20), CreateImageBytes());

        new PdfSharpRenderer().Render([command], new PageSettings(), output);

        Assert.True(output.Length > 0);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(output.GetBuffer(), 0, 5));
    }

    [Fact]
    public void DrawCommandGenerator_preserves_wrapped_text_style()
    {
        var style = CellStyle.Default with { WrapText = true };
        var document = new RenderDocument([new RenderPage(1,
            [new(new("長い文字列", style), new(0, 0, 10, 10))])]);

        var command = Assert.IsType<DrawTextCommand>(Assert.Single(new DrawCommandGeneratorPass().Generate(document)));

        Assert.True(command.Style.WrapText);
    }

    [Fact]
    public void DrawCommandGenerator_preserves_shrink_to_fit_style()
    {
        var style = CellStyle.Default with { ShrinkToFit = true };
        var document = new RenderDocument([new RenderPage(1,
            [new(new("長い文字列", style), new(0, 0, 10, 10))])]);

        var command = Assert.IsType<DrawTextCommand>(Assert.Single(new DrawCommandGeneratorPass().Generate(document)));

        Assert.True(command.Style.ShrinkToFit);
    }

    [Fact]
    public void PdfSharpFontResolver_returns_the_configured_font_file()
    {
        var fontFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ttf");
        var fontData = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(fontFilePath, fontData);

        try
        {
            var resolver = new PdfSharpFontResolver("Noto Sans JP", fontFilePath);

            var font = resolver.ResolveTypeface("Noto Sans JP", bold: false, italic: false);

            Assert.NotNull(font);
            Assert.Equal(Path.GetFullPath(fontFilePath), font.FaceName);
            Assert.Equal(fontData, resolver.GetFont(font.FaceName));
            Assert.Null(resolver.ResolveTypeface("Other Font", bold: false, italic: false));
        }
        finally
        {
            File.Delete(fontFilePath);
        }
    }

    [Fact]
    public void ResolvePrintAreaPass_expands_range_to_cover_merged_cell_spans()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("merged", CellStyle.Default, RowSpan: 2, ColumnSpan: 3)
            },
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(50), [2] = new(60), [3] = new(70) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(20), [2] = new(25) });

        new ResolvePrintAreaPass().Execute(context);

        Assert.Equal(new CellRange(new(1, 1), new(2, 3)), context.PrintArea);
    }

    [Fact]
    public void ExcelReader_reads_column_and_row_definitions_for_merged_range_extents()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Cell(1, 1).Value = "merged";
                worksheet.Range(1, 1, 1, 3).Merge();
                workbook.SaveAs(path);
            }

            var sheet = new ExcelReader().Read(path).Sheets[0];

            Assert.True(sheet.Columns.ContainsKey(2), "結合範囲内の列2の定義が必要です");
            Assert.True(sheet.Columns.ContainsKey(3), "結合範囲内の列3の定義が必要です");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ReportLayoutContext CreateContext(
        IReadOnlyDictionary<CellAddress, ReportCell>? cells = null,
        IReadOnlyDictionary<int, ColumnDefinition>? columns = null,
        IReadOnlyDictionary<int, RowDefinition>? rows = null,
        PageSettings? pageSettings = null,
        IReadOnlyList<ReportImage>? images = null,
        HeaderFooter? headerFooter = null) =>
        new(new ReportSheet("Sheet1",
            cells ?? new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = new(null, CellStyle.Default) },
            columns ?? new Dictionary<int, ColumnDefinition> { [1] = new() },
            rows ?? new Dictionary<int, RowDefinition> { [1] = new() },
            [], pageSettings ?? new(), Images: images, HeaderFooter: headerFooter), new FixedTextMeasurer());

    private static byte[] CreateImageBytes()
    {
        using var bitmap = new SKBitmap(1, 1);
        bitmap.SetPixel(0, 0, SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private sealed class FixedTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => new(10, 10);
    }
}

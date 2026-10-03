using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using ExcelRenderer.Rendering;
using PdfSharp.Fonts;
using SkiaSharp;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>
/// Excel の読み取りからページ分割と描画命令生成までのレイアウト処理を検証します。
/// </summary>
public sealed class LayoutPassTests
{
    /// <summary>連続レイアウトが印刷範囲とページ設定を無視し、元の座標を保持することを検証します。</summary>
    [Fact]
    public void LayoutContinuous_uses_used_range_without_print_scaling_or_margins()
    {
        var sheet = new ReportSheet(
            "Sheet",
            new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("left", CellStyle.Default),
                [new(1, 2)] = new("right", CellStyle.Default),
            },
            new Dictionary<int, ColumnDefinition> { [1] = new(50), [2] = new(70) },
            new Dictionary<int, RowDefinition> { [1] = new(20) },
            [],
            new(70, 100, 10, 10, 10, 10, Scale: 0.5),
            new(new(1, 1), new(1, 1)));

        var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer()).LayoutContinuous(sheet);

        var page = Assert.Single(layout.Document.Pages);
        Assert.Equal(120, layout.Width);
        Assert.Equal(20, layout.Height);
        Assert.Equal(new ReportRect(50, 0, 70, 20), page.Cells.Single(cell => cell.Cell.Text == "right").Bounds);
    }

    /// <summary>
    /// 各列の X 座標が先行列の幅を累積した位置に設定されることを検証します。
    /// </summary>
    [Fact]
    public void ColumnLayoutPass_assigns_cumulative_positions()
    {
        var context = CreateContext(columns: new Dictionary<int, ColumnDefinition>
        {
            [1] = new(80), [2] = new(120), [3] = new(60),
        });
        context.PrintArea = new(new(1, 1), new(1, 3));
        new HiddenRowColumnPass().Execute(context);

        new ColumnLayoutPass().Execute(context);

        Assert.Equal(0, context.ColumnLayouts[1].X);
        Assert.Equal(80, context.ColumnLayouts[2].X);
        Assert.Equal(200, context.ColumnLayouts[3].X);
    }

    /// <summary>
    /// 結合セルの境界が結合対象の全行列にまたがる寸法となることを検証します。
    /// </summary>
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

    /// <summary>
    /// 横幅がページを超えるシートが列境界で分割されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_splits_wide_sheets_at_column_boundaries()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("left", CellStyle.Default),
                [new(1, 2)] = new("right", CellStyle.Default),
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

    /// <summary>
    /// 印刷倍率がページ分割とページ内コンテンツの寸法の両方に適用されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_applies_print_scale_to_content_and_pagination()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("left", CellStyle.Default),
                [new(1, 2)] = new("right", CellStyle.Default),
            },
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(50), [2] = new(50) },
            pageSettings: new(70, 100, 10, 10, 10, 10, Scale: 0.5));
        context.PrintArea = new(new(1, 1), new(1, 2));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        var page = Assert.Single(context.RenderDocument!.Pages);
        Assert.Equal(2, page.Cells.Count);
        Assert.Equal(new ReportRect(10, 10, 25, 7.5), page.Cells[0].Bounds);
        Assert.Equal(new ReportRect(35, 10, 25, 7.5), page.Cells[1].Bounds);
        Assert.Equal(5, page.Cells[0].Cell.Style.Font.Size);
    }

    /// <summary>
    /// 指定した横・縦ページ数に収まる倍率が計算されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_fits_content_to_requested_page_count()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("top left", CellStyle.Default),
                [new(1, 2)] = new("top right", CellStyle.Default),
                [new(2, 1)] = new("bottom left", CellStyle.Default),
                [new(2, 2)] = new("bottom right", CellStyle.Default),
            },
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(50), [2] = new(50) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(40), [2] = new(40) },
            pageSettings: new(
                70,
                60,
                10,
                10,
                10,
                10,
                Scale: null,
                FitToPagesWide: 1,
                FitToPagesTall: 1));
        context.PrintArea = new(new(1, 1), new(2, 2));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        var page = Assert.Single(context.RenderDocument!.Pages);
        Assert.Equal(4, page.Cells.Count);
        Assert.Equal(new ReportRect(35, 30, 25, 20), page.Cells[3].Bounds);
    }

    /// <summary>
    /// ページ数への適合で既に収まっている内容が拡大されないことを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_does_not_enlarge_content_when_fitting()
    {
        var context = CreateContext(
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(100) },
            pageSettings: new(
                520,
                100,
                10,
                10,
                10,
                10,
                Scale: null,
                FitToPagesWide: 1));
        context.PrintArea = new(new(1, 1), new(1, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        var cell = Assert.Single(Assert.Single(context.RenderDocument!.Pages).Cells);
        Assert.Equal(100, cell.Bounds.Width);
        Assert.Equal(10, cell.Cell.Style.Font.Size);
    }

    /// <summary>
    /// 単一の巨大な列も指定ページ数へ収める倍率の判定対象になることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_fits_a_single_oversized_column()
    {
        var context = CreateContext(
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(1000) },
            pageSettings: new(
                520,
                100,
                10,
                10,
                10,
                10,
                Scale: null,
                FitToPagesWide: 1));
        context.PrintArea = new(new(1, 1), new(1, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        var cell = Assert.Single(Assert.Single(context.RenderDocument!.Pages).Cells);
        Assert.Equal(500, cell.Bounds.Width, 6);
    }

    /// <summary>
    /// ページ倍率が図形の線幅、文字サイズ、および内側余白にも一貫して適用されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_scales_shape_lengths_with_shape_bounds()
    {
        var shape = new ReportShape(
            new(1, 1),
            0,
            0,
            40,
            20,
            ShapeKind.Rectangle,
            new(null, new ReportColor(0, 0, 0), 2),
            new("shape", new FontStyle(Size: 12), HorizontalAlignment.Left, VerticalAlignment.Top, true, 4, 6, 8, 10),
            0,
            0);
        var context = CreateContext(
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(80) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(30) },
            pageSettings: new(100, 100, 10, 10, 10, 10, Scale: 0.5),
            shapes: [shape]);
        context.PrintArea = new(new(1, 1), new(1, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        var rendered = Assert.Single(Assert.Single(context.RenderDocument!.Pages).Shapes!);
        Assert.Equal(20, rendered.Bounds.Width);
        Assert.Equal(1, rendered.Shape.Style.LineWidth);
        Assert.Equal(6, rendered.Shape.Text!.Font.Size);
        Assert.Equal(2, rendered.Shape.Text.MarginLeft);
        Assert.Equal(5, rendered.Shape.Text.MarginBottom);
    }

    /// <summary>
    /// 結合セルが改ページ位置で分断されず同じページに配置されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_keeps_merged_cells_on_one_page()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("merged", CellStyle.Default, 2),
                [new(3, 1)] = new("next", CellStyle.Default),
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

    /// <summary>
    /// 行の途中では改ページされず、行全体がいずれかのページに配置されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_does_not_split_rows()
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("one", CellStyle.Default),
            [new(2, 1)] = new("two", CellStyle.Default),
        };
        var context = CreateContext(
            cells: cells,
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

    /// <summary>明示倍率モードで保存された手動改ページが行バンドを分割することを検証します。</summary>
    [Fact]
    public void PaginationPass_honors_manual_row_breaks_in_explicit_mode()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("one", CellStyle.Default),
                [new(2, 1)] = new("two", CellStyle.Default),
                [new(3, 1)] = new("three", CellStyle.Default),
            },
            rows: new Dictionary<int, RowDefinition> { [1] = new(10), [2] = new(10), [3] = new(10) },
            pageSettings: new PageSettings(100, 100, 10, 10, 10, 10)
            {
                ScaleMode = PrintScaleMode.Explicit,
                ManualRowBreaks = [1],
            });
        context.PrintArea = new(new(1, 1), new(3, 1));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        Assert.Equal(2, context.RenderDocument!.Pages.Count);
        Assert.Equal("one", Assert.Single(context.RenderDocument.Pages[0].Cells).Cell.Text);
        Assert.Equal(["two", "three"], context.RenderDocument.Pages[1].Cells.Select(cell => cell.Cell.Text));
    }

    /// <summary>離れた複数印刷範囲が外接矩形ではなく独立ページとして生成されることを検証します。</summary>
    [Fact]
    public void ReportLayoutEngine_paginates_multiple_print_areas_independently()
    {
        var sheet = new ReportSheet(
            "Sheet1",
            new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("first", CellStyle.Default),
                [new(1, 3)] = new("second", CellStyle.Default),
            },
            new Dictionary<int, ColumnDefinition> { [1] = new(20), [2] = new(20), [3] = new(20) },
            new Dictionary<int, RowDefinition> { [1] = new(15) },
            [],
            new())
        {
            PrintAreas = [new(new(1, 1), new(1, 1)), new(new(1, 3), new(1, 3))],
        };

        var pages = new ReportLayoutEngine(new FixedTextMeasurer()).Layout(sheet).Pages;

        Assert.Equal(2, pages.Count);
        Assert.Equal("first", Assert.Single(pages[0].Cells).Cell.Text);
        Assert.Equal("second", Assert.Single(pages[1].Cells).Cell.Text);
        Assert.Equal([1, 2], pages.Select(page => page.Number));
    }

    /// <summary>
    /// 印刷タイトルに指定した行と列が各ページで繰り返されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_repeats_title_rows_and_columns_on_each_page()
    {
        var cells = new Dictionary<CellAddress, ReportCell>();
        for (var row = 1; row <= 3; row++)
        {
            for (var column = 1; column <= 3; column++)
            {
                cells[new(row, column)] = new($"{row},{column}", CellStyle.Default);
            }
        }

        var context = CreateContext(
            cells: cells,
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(20), [2] = new(40), [3] = new(40) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(10), [2] = new(30), [3] = new(30) },
            pageSettings: new(80, 70, 10, 10, 10, 10, TitleRows: new(1, 1), TitleColumns: new(1, 1)));
        context.PrintArea = new(new(1, 1), new(3, 3));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        Assert.Equal(4, context.RenderDocument!.Pages.Count);
        var lastPage = context.RenderDocument.Pages[3];
        Assert.Equal(["1,1", "1,3", "3,1", "3,3"], lastPage.Cells.Select(cell => cell.Cell.Text));
        Assert.Equal(new ReportRect(10, 10, 20, 10), lastPage.Cells[0].Bounds);
        Assert.Equal(new ReportRect(30, 20, 40, 30), lastPage.Cells[3].Bounds);
    }

    /// <summary>
    /// ページ数に合わせる倍率計算で繰り返し印刷するタイトル領域が考慮されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_accounts_for_repeated_titles_when_fitting_page_counts()
    {
        var cells = new Dictionary<CellAddress, ReportCell>();
        var columns = new Dictionary<int, ColumnDefinition>();
        var rows = new Dictionary<int, RowDefinition>();
        for (var index = 1; index <= 10; index++)
        {
            columns[index] = new(10);
            rows[index] = new(10);
            for (var column = 1; column <= 10; column++)
            {
                cells[new(index, column)] = new($"{index},{column}", CellStyle.Default);
            }
        }

        var context = CreateContext(
            cells: cells,
            columns: columns,
            rows: rows,
            pageSettings: new(
                70,
                70,
                10,
                10,
                10,
                10,
                Scale: null,
                FitToPagesWide: 2,
                FitToPagesTall: 2,
                TitleRows: new(1, 1),
                TitleColumns: new(1, 1)));
        context.PrintArea = new(new(1, 1), new(10, 10));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        Assert.Equal(4, context.RenderDocument!.Pages.Count);
        var lastPage = context.RenderDocument.Pages[3];
        Assert.Contains(lastPage.Cells, cell => cell.Cell.Text == "1,1");
        Assert.Contains(lastPage.Cells, cell => cell.Cell.Text == "10,10");
    }

    /// <summary>
    /// 画像がアンカーセルを基準としたページ内座標へ配置されることを検証します。
    /// </summary>
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

    /// <summary>複数ページに交差する画像が各ページへ同じ倍率で配置されることを検証します。</summary>
    [Fact]
    public void PaginationPass_places_cross_page_images_on_each_intersecting_page()
    {
        var imageBytes = CreateImageBytes();
        var context = CreateContext(
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(30), [2] = new(30) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(20) },
            pageSettings: new(50, 50, 10, 10, 10, 10),
            images: [new(new(1, 1), 0, 0, 50, 10, imageBytes)]);
        context.PrintArea = new(new(1, 1), new(1, 2));
        new HiddenRowColumnPass().Execute(context);
        new ColumnLayoutPass().Execute(context);
        new RowLayoutPass().Execute(context);
        new TextMeasurePass().Execute(context);
        new CellBoundsPass().Execute(context);

        new PaginationPass().Execute(context);

        Assert.Equal(2, context.RenderDocument!.Pages.Count);
        Assert.All(context.RenderDocument.Pages, page => Assert.Single(page.Images!));
        Assert.Equal(50, context.RenderDocument.Pages[0].Images![0].Bounds.Width);
        Assert.Equal(-20, context.RenderDocument.Pages[1].Images![0].Bounds.X);
    }

    /// <summary>
    /// ワークシートに埋め込まれた画像のデータ、位置および寸法が読み取られることを検証します。
    /// </summary>
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

            var sheet = new ExcelReader().Read(path).Sheets[0];
            var image = Assert.Single(sheet.Images!);

            Assert.Equal(new CellAddress(2, 3), image.Anchor);
            Assert.Equal(3, image.OffsetX);
            Assert.Equal(3.75, image.OffsetY);
            Assert.Equal(imageBytes, image.ImageBytes);
            Assert.True(sheet.Columns.ContainsKey(3));
            Assert.True(sheet.Rows.ContainsKey(2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>DrawingML画像のアンカー、crop、および変換情報がモデルへ保持されることを検証します。</summary>
    [Fact]
    public void ExcelReader_preserves_picture_anchor_crop_and_transform()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Pictures.Add(new MemoryStream(CreateImageBytes())).MoveTo(worksheet.Cell(2, 3), 4, 5);
                workbook.SaveAs(path);
            }

            using (var document = SpreadsheetDocument.Open(path, true))
            {
                var picture = document.WorkbookPart!.WorksheetParts.Single().DrawingsPart!
                    .WorksheetDrawing.Descendants<Xdr.Picture>().Single();
                picture.BlipFill!.SourceRectangle = new A.SourceRectangle { Left = 10000, Right = 20000 };
                picture.ShapeProperties!.Transform2D!.Rotation = 900000;
                picture.ShapeProperties.Transform2D.HorizontalFlip = true;
                picture.Ancestors<Xdr.WorksheetDrawing>().Single().Save();
            }

            var image = Assert.Single(new ExcelReader().Read(path).Sheets[0].Images!);

            Assert.NotNull(image.DrawingAnchor);
            Assert.Equal(DrawingAnchorKind.OneCell, image.DrawingAnchor!.Kind);
            Assert.Equal(0.1, image.Crop!.Left);
            Assert.Equal(0.2, image.Crop.Right);
            Assert.Equal(15, image.Rotation);
            Assert.True(image.FlipHorizontal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>確定した改行とfont runが描画命令まで共有されることを検証します。</summary>
    [Fact]
    public void TextLayout_is_finalized_once_and_carried_to_draw_commands()
    {
        var fontManager = new MissingPrivateUseFontManager();
        var sheet = new ReportSheet(
            "Sheet1",
            new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("A😀B\nC", CellStyle.Default with { WrapText = true }),
            },
            new Dictionary<int, ColumnDefinition> { [1] = new(20) },
            new Dictionary<int, RowDefinition> { [1] = new(40) },
            [],
            new());

        var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer(fontManager)).Layout(sheet);
        var command = Assert.IsType<DrawTextCommand>(Assert.Single(new DrawCommandGeneratorPass().Generate(layout)));

        Assert.NotNull(command.TextLayout);
        Assert.True(command.TextLayout!.Lines.Count >= 2);
        Assert.Contains(command.TextLayout.Lines, line => line.ExplicitBreak);
        Assert.All(command.TextLayout.Lines, line => Assert.NotNull(line.Runs));
    }

    /// <summary>
    /// ワークシートのヘッダーとフッターの各セクションが読み取られることを検証します。
    /// </summary>
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
            Assert.Null(headerFooter.FirstPageHeader);
            Assert.Null(headerFooter.FirstPageFooter);
            Assert.Null(headerFooter.EvenPageHeader);
            Assert.Null(headerFooter.EvenPageFooter);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 通常ヘッダーとフッターが分割後のすべてのページに描画されることを検証します。
    /// </summary>
    [Fact]
    public void ExcelReader_regular_header_and_footer_are_rendered_on_every_page()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Cell(1, 1).Value = "one";
                worksheet.Cell(2, 1).Value = "two";
                worksheet.Row(1).Height = 40;
                worksheet.Row(2).Height = 40;
                worksheet.PageSetup.PaperSize = XLPaperSize.LetterPaper;
                worksheet.PageSetup.Margins.Top = 0.1;
                worksheet.PageSetup.Margins.Bottom = 0.1;
                worksheet.PageSetup.Header.Center.AddText("ページ &P / &N");
                worksheet.PageSetup.Footer.Center.AddText("フッター &P / &N");
                workbook.SaveAs(path);
            }

            var sheet = new ExcelReader().Read(path).Sheets[0] with
            {
                PageSettings = new(100, 70, 10, 10, 10, 10),
            };
            var pages = new ReportLayoutEngine(new FixedTextMeasurer()).Layout(sheet).Pages;

            Assert.Equal(2, pages.Count);
            Assert.Equal(["ページ 1 / 2", "フッター 1 / 2"], pages[0].HeaderFooterTexts!.Select(text => text.Text));
            Assert.Equal(["ページ 2 / 2", "フッター 2 / 2"], pages[1].HeaderFooterTexts!.Select(text => text.Text));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 印刷設定が読み取られ、Excel の列幅が描画用の幅へ変換されることを検証します。
    /// </summary>
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

    /// <summary>
    /// SpreadsheetML の raw 列幅が仕様の最大数字幅の式でポイントへ変換されることを検証します。
    /// </summary>
    [Theory]
    [InlineData(8.7109375, 7, 45.75)]
    [InlineData(0, 7, 0)]
    [InlineData(0.5, 7, 2.25)]
    public void ColumnWidthCalculator_converts_raw_width_to_points(double rawWidth, double mdw, double expected)
    {
        Assert.Equal(expected, ColumnWidthCalculator.ToPoints(rawWidth, mdw), 6);
    }

    /// <summary>
    /// Normal スタイルのテーマフォントを実フォントへ解決してMDWを計測することを検証します。
    /// </summary>
    [Fact]
    public void ExcelReader_measures_maximum_digit_width_from_normal_theme_font()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Cell(1, 1).Value = "value";
                worksheet.Column(1).Width = 8;
                workbook.SaveAs(path);
            }

            var diagnostics = new DiagnosticCollector(new DiagnosticOptions());
            var document = new ExcelReader(new MissingPrivateUseFontManager())
                .Read(File.ReadAllBytes(path), diagnostics);

            Assert.NotEmpty(document.Sheets[0].Columns);
            Assert.Contains(diagnostics.ToArray(), diagnostic => diagnostic.Code == "MaximumDigitWidthResolved");
            Assert.DoesNotContain(diagnostics.ToArray(), diagnostic => diagnostic.Code == "MaximumDigitWidthFallback");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// パーセント指定の印刷倍率がワークシートから読み取られることを検証します。
    /// </summary>
    [Fact]
    public void ExcelReader_reads_percentage_print_scale()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Cell(1, 1).Value = "scaled";
                worksheet.PageSetup.AdjustTo(75);
                workbook.SaveAs(path);
            }

            var settings = new ExcelReader().Read(path).Sheets[0].PageSettings;

            Assert.Equal(0.75, settings.Scale);
            Assert.Null(settings.FitToPagesWide);
            Assert.Null(settings.FitToPagesTall);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 繰り返し印刷するタイトル行とタイトル列の範囲が読み取られることを検証します。
    /// </summary>
    [Fact]
    public void ExcelReader_reads_print_title_rows_and_columns()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Cell(1, 1).Value = "title";
                worksheet.PageSetup.SetRowsToRepeatAtTop(1, 2);
                worksheet.PageSetup.SetColumnsToRepeatAtLeft(1, 3);
                workbook.SaveAs(path);
            }

            var settings = new ExcelReader().Read(path).Sheets[0].PageSettings;

            Assert.Equal(new IndexRange(1, 2), settings.TitleRows);
            Assert.Equal(new IndexRange(1, 3), settings.TitleColumns);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 指定ページ数に合わせる印刷設定が読み取られることを検証します。
    /// </summary>
    [Fact]
    public void ExcelReader_reads_fit_to_pages_print_scale()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.AddWorksheet("Sheet1");
                worksheet.Cell(1, 1).Value = "fitted";
                worksheet.PageSetup.FitToPages(1, 2);
                workbook.SaveAs(path);
            }

            var settings = new ExcelReader().Read(path).Sheets[0].PageSettings;

            Assert.Null(settings.Scale);
            Assert.Equal(1, settings.FitToPagesWide);
            Assert.Equal(2, settings.FitToPagesTall);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// raw XML の Fit モードが残存する明示倍率より優先されることを検証します。
    /// </summary>
    [Fact]
    public void ExcelReader_uses_raw_fit_mode_when_scale_is_also_present()
    {
        var path = CreateWorkbookWithPageSetup(fitToPage: true, scale: 75, fitToWidth: 1, fitToHeight: 0);
        try
        {
            var settings = new ExcelReader().Read(path).Sheets[0].PageSettings;

            Assert.Equal(PrintScaleMode.FitToPages, settings.ScaleMode);
            Assert.Null(settings.Scale);
            Assert.Equal(1, settings.FitToPagesWide);
            Assert.Equal(0, settings.FitToPagesTall);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// raw XML で Fit が無効ならページ数属性があっても明示倍率を使用することを検証します。
    /// </summary>
    [Fact]
    public void ExcelReader_uses_raw_explicit_scale_when_fit_mode_is_disabled()
    {
        var path = CreateWorkbookWithPageSetup(fitToPage: false, scale: 75, fitToWidth: 1, fitToHeight: 1);
        try
        {
            var settings = new ExcelReader().Read(path).Sheets[0].PageSettings;

            Assert.Equal(PrintScaleMode.Explicit, settings.ScaleMode);
            Assert.Equal(0.75, settings.Scale);
            Assert.Null(settings.FitToPagesWide);
            Assert.Null(settings.FitToPagesTall);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Fit のページ数属性が省略された場合にスキーマ既定値の各1ページを使用することを検証します。
    /// </summary>
    [Fact]
    public void ExcelReader_defaults_missing_fit_page_counts_to_one()
    {
        var path = CreateWorkbookWithPageSetup(fitToPage: true, scale: 75, fitToWidth: null, fitToHeight: null);
        try
        {
            var settings = new ExcelReader().Read(path).Sheets[0].PageSettings;

            Assert.Equal(PrintScaleMode.FitToPages, settings.ScaleMode);
            Assert.Equal(1, settings.FitToPagesWide);
            Assert.Equal(1, settings.FitToPagesTall);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 空の結合セル範囲が左上セルを基準とする一つのセルへ正規化されることを検証します。
    /// </summary>
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

    /// <summary>
    /// ページ番号などのフィールドを解決したヘッダー・フッターテキストがページに追加されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_adds_header_and_footer_texts_with_resolved_fields()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("one", CellStyle.Default),
                [new(2, 1)] = new("two", CellStyle.Default),
            },
            rows: new Dictionary<int, RowDefinition> { [1] = new(40), [2] = new(40) },
            pageSettings: new(100, 90, 10, 10, 10, 10),
            headerFooter: new(new("左 &A"), new(string.Empty, "ページ &P / &N")));
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

    /// <summary>
    /// 先頭ページ用と偶数ページ用のヘッダーが該当ページで選択されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_uses_first_and_even_page_headers()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("one", CellStyle.Default),
                [new(2, 1)] = new("two", CellStyle.Default),
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

    /// <summary>
    /// セルがないシートでもヘッダーとフッターだけのページが生成されることを検証します。
    /// </summary>
    [Fact]
    public void PaginationPass_renders_header_and_footer_without_cells()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>(),
            headerFooter: new(new("ヘッダー"), new("フッター")));

        new PaginationPass().Execute(context);

        var page = Assert.Single(context.RenderDocument!.Pages);
        Assert.Equal(["ヘッダー", "フッター"], page.HeaderFooterTexts!.Select(text => text.Text));
    }

    /// <summary>
    /// セルの塗りつぶし、罫線、テキストの順に描画命令が生成されることを検証します。
    /// </summary>
    [Fact]
    public void DrawCommandGenerator_orders_fill_before_border_before_text()
    {
        var style = CellStyle.Default with
        {
            Background = new(1, 2, 3),
            Border = new(new BorderSide()),
        };
        var document = new RenderDocument(
        [
            new RenderPage(1, [new(new("text", style), new(0, 0, 10, 10))])
        ]);

        var commands = new DrawCommandGeneratorPass().Generate(document);

        Assert.Collection(
            commands,
            command => Assert.IsType<FillRectangleCommand>(command),
            command => Assert.IsType<DrawBorderCommand>(command),
            command => Assert.IsType<DrawTextCommand>(command));
    }

    /// <summary>
    /// セルテキストの描画領域が罫線から内側へ余白を取ることを検証します。
    /// </summary>
    [Fact]
    public void DrawCommandGenerator_insets_cell_text_from_borders()
    {
        var document = new RenderDocument(
        [
            new RenderPage(1, [new(new("text", CellStyle.Default), new(10, 20, 30, 40))])
        ]);

        var command = Assert.IsType<DrawTextCommand>(Assert.Single(new DrawCommandGeneratorPass().Generate(document)));

        Assert.Equal(new ReportRect(10.5, 20.5, 29, 39), command.Bounds);
    }

    /// <summary>セルのインデントが文字の内容領域へ反映されることを検証します。</summary>
    [Fact]
    public void DrawCommandGenerator_applies_cell_indent_to_content_bounds()
    {
        var style = CellStyle.Default with { Indent = 2 };
        var document = new RenderDocument(
        [
            new RenderPage(1, [new(new("text", style), new(10, 20, 30, 40))])
        ]);

        var command = Assert.IsType<DrawTextCommand>(Assert.Single(new DrawCommandGeneratorPass().Generate(document)));

        Assert.Equal(new ReportRect(20.5, 20.5, 19, 39), command.Bounds);
    }

    /// <summary>
    /// 画像の描画命令がセル内容の描画命令より後に追加されることを検証します。
    /// </summary>
    [Fact]
    public void DrawCommandGenerator_adds_images_after_cell_content()
    {
        var imageBytes = CreateImageBytes();
        var document = new RenderDocument(
        [
            new RenderPage(
                1,
                [new(new("text", CellStyle.Default), new(0, 0, 10, 10))],
                [new(new(10, 20, 30, 40), imageBytes)])
        ]);

        var commands = new DrawCommandGeneratorPass().Generate(document);

        var image = Assert.IsType<DrawImageCommand>(commands[commands.Count - 1]);
        Assert.Equal(new ReportRect(10, 20, 30, 40), image.Bounds);
        Assert.Equal(imageBytes, image.ImageBytes);
    }

    /// <summary>
    /// 描画文書が有効な PDF データとしてストリームへ書き込まれることを検証します。
    /// </summary>
    [Fact]
    public void PdfSharpRenderer_writes_a_pdf_document()
    {
        using var output = new MemoryStream();

        new PdfSharpRenderer().Render([], new PageSettings(), output);

        Assert.True(output.Length > 0);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(output.GetBuffer(), 0, 5));
    }

    /// <summary>
    /// 埋め込み画像が PDF ページへ描画されることを検証します。
    /// </summary>
    [Fact]
    public void PdfSharpRenderer_renders_an_image()
    {
        using var output = new MemoryStream();
        var command = new DrawImageCommand(1, new(0, 0, 20, 20), CreateImageBytes());

        new PdfSharpRenderer().Render([command], new PageSettings(), output);

        Assert.True(output.Length > 0);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(output.GetBuffer(), 0, 5));
    }

    /// <summary>CBDT カラー絵文字を PDF に画像として描画できます。</summary>
    [Fact]
    public void PdfSharpRenderer_renders_color_emoji()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FallbackFamilies = [] });
        using var output = new MemoryStream();
        new PdfSharpRenderer(manager).Render(
            [new DrawTextCommand(1, new(4, 4, 80, 58), "😀", CellStyle.Default with
            { Font = new FontStyle("Noto Sans JP", 40) })],
            new PageSettings(88, 66), output);

        Assert.True(output.Length > 0);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(output.GetBuffer(), 0, 5));
    }

    /// <summary>欠落した私用文字の置換を PDF の解決済みテキスト描画経路へ渡します。</summary>
    [Fact]
    public void PdfSharpRenderer_uses_resolved_runs_for_missing_private_use_glyphs()
    {
        var manager = new MissingPrivateUseFontManager();
        var resolver = new CountingFontResolver();
        using var output = new MemoryStream();
        GlobalFontSettings.ResetFontManagement();
        GlobalFontSettings.FontResolver = resolver;
        try
        {
            var measured = new PdfSharpTextMeasurer(manager).Measure("\uE000", new FontStyle("Missing Font", 12), 80, false);
            new PdfSharpRenderer(manager).Render(
                [new DrawTextCommand(1, new(4, 4, 80, 20), "\uE000", CellStyle.Default with
                { Font = new FontStyle("Missing Font", 12) })],
                new PageSettings(88, 28), output);

            Assert.True(measured.Width > 0);
            Assert.True(manager.ResolveTextRunsCallCount > 1);
            Assert.Equal(0, resolver.ResolveTypefaceCallCount);
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(output.GetBuffer(), 0, 5));
        }
        finally
        {
            TestAssembly.Initialize();
        }
    }

    /// <summary>
    /// 折り返し指定がテキスト描画命令のスタイルに保持されることを検証します。
    /// </summary>
    [Fact]
    public void DrawCommandGenerator_preserves_wrapped_text_style()
    {
        var style = CellStyle.Default with { WrapText = true };
        var document = new RenderDocument([new RenderPage(
            1,
            [new(new("長い文字列", style), new(0, 0, 10, 10))])]);

        var command = Assert.IsType<DrawTextCommand>(Assert.Single(new DrawCommandGeneratorPass().Generate(document)));

        Assert.True(command.Style.WrapText);
    }

    /// <summary>
    /// 縮小表示指定がテキスト描画命令のスタイルに保持されることを検証します。
    /// </summary>
    [Fact]
    public void DrawCommandGenerator_preserves_shrink_to_fit_style()
    {
        var style = CellStyle.Default with { ShrinkToFit = true };
        var document = new RenderDocument([new RenderPage(
            1,
            [new(new("長い文字列", style), new(0, 0, 10, 10))])]);

        var command = Assert.IsType<DrawTextCommand>(Assert.Single(new DrawCommandGeneratorPass().Generate(document)));

        Assert.True(command.Style.ShrinkToFit);
    }

    /// <summary>
    /// 登録したフォントファミリに対して設定済みフォントファイルのバイト列が返されることを検証します。
    /// </summary>
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

            var aliases = new PdfSharpFontResolver("Noto Sans JP", fontFilePath, "游ゴシック", "Yu Gothic");
            Assert.Equal(font.FaceName, aliases.ResolveTypeface("游ゴシック", false, false)!.FaceName);
            Assert.Equal(font.FaceName, aliases.ResolveTypeface("yu gothic", false, false)!.FaceName);
            Assert.Null(aliases.ResolveTypeface("Other Font", false, false));
        }
        finally
        {
            File.Delete(fontFilePath);
        }
    }

    /// <summary>既定のリゾルバーが NuGet アセンブリに内蔵した静的フォントを返すことを検証します。</summary>
    [Fact]
    public void PdfSharpFontResolver_returns_bundled_regular_font()
    {
        var resolver = new PdfSharpFontResolver();
        var face = resolver.ResolveTypeface("游ゴシック", bold: false, italic: false);

        Assert.NotNull(face);
        var data = resolver.GetFont(face.FaceName);
        Assert.NotNull(data);
        Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf")), data);
        using var stream = new MemoryStream(data);
        using var typeface = SKTypeface.FromStream(stream);
        Assert.Equal("Noto Sans JP", typeface.FamilyName);
    }

    /// <summary>
    /// 印刷範囲が結合セルの終端まで拡張されることを検証します。
    /// </summary>
    [Fact]
    public void ResolvePrintAreaPass_expands_range_to_cover_merged_cell_spans()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>
            {
                [new(1, 1)] = new("merged", CellStyle.Default, RowSpan: 2, ColumnSpan: 3),
            },
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(50), [2] = new(60), [3] = new(70) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(20), [2] = new(25) });

        new ResolvePrintAreaPass().Execute(context);

        Assert.Equal(new CellRange(new(1, 1), new(2, 3)), context.PrintArea);
    }

    /// <summary>
    /// セルがなく画像だけのシートから描画ページが生成されることを検証します。
    /// </summary>
    [Fact]
    public void ReportLayoutEngine_renders_an_image_only_sheet()
    {
        var imageBytes = CreateImageBytes();
        var sheet = new ReportSheet(
            "Sheet1",
            new Dictionary<CellAddress, ReportCell>(),
            new Dictionary<int, ColumnDefinition> { [3] = new(80) },
            new Dictionary<int, RowDefinition> { [2] = new(20) },
            [],
            new(),
            Images: [new(new(2, 3), 0, 0, 10, 11, imageBytes)]);

        var page = Assert.Single(new ReportLayoutEngine(new FixedTextMeasurer()).Layout(sheet).Pages);

        var image = Assert.Single(page.Images!);
        Assert.Equal(new ReportRect(36, 36, 10, 11), image.Bounds);
        Assert.Equal(imageBytes, image.ImageBytes);
    }

    /// <summary>
    /// セル範囲外にある画像を含むよう印刷範囲が拡張されることを検証します。
    /// </summary>
    [Fact]
    public void ResolvePrintAreaPass_expands_range_to_cover_an_image_outside_cells()
    {
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = new("text", CellStyle.Default) },
            columns: new Dictionary<int, ColumnDefinition> { [1] = new(80), [3] = new(80) },
            rows: new Dictionary<int, RowDefinition> { [1] = new(20), [2] = new(20) },
            images: [new(new(2, 3), 0, 0, 10, 11, CreateImageBytes())]);

        new ResolvePrintAreaPass().Execute(context);

        Assert.Equal(new CellRange(new(1, 1), new(2, 3)), context.PrintArea);
    }

    /// <summary>
    /// 図形だけのシートで図形アンカーから印刷範囲が決定されることを検証します。
    /// </summary>
    [Fact]
    public void ResolvePrintAreaPass_uses_shape_anchors_for_shape_only_sheets()
    {
        var shape = new ReportShape(
            new(4, 6),
            0,
            0,
            20,
            10,
            ShapeKind.Rectangle,
            new(new ReportColor(255, 0, 0), null, 0),
            null,
            0,
            0);
        var context = CreateContext(
            cells: new Dictionary<CellAddress, ReportCell>(),
            columns: new Dictionary<int, ColumnDefinition> { [6] = new(80) },
            rows: new Dictionary<int, RowDefinition> { [4] = new(20) },
            shapes: [shape]);

        new ResolvePrintAreaPass().Execute(context);

        Assert.Equal(new CellRange(new(4, 6), new(4, 6)), context.PrintArea);
    }

    /// <summary>
    /// セルと画像を含むシートから両方の描画命令が生成されることを検証します。
    /// </summary>
    [Fact]
    public void ReportLayoutEngine_renders_cells_and_images()
    {
        var imageBytes = CreateImageBytes();
        var sheet = new ReportSheet(
            "Sheet1",
            new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = new("text", CellStyle.Default) },
            new Dictionary<int, ColumnDefinition> { [1] = new(80) },
            new Dictionary<int, RowDefinition> { [1] = new(20) },
            [],
            new(),
            Images: [new(new(1, 1), 0, 0, 10, 11, imageBytes)]);

        var page = Assert.Single(new ReportLayoutEngine(new FixedTextMeasurer()).Layout(sheet).Pages);

        Assert.Single(page.Cells);
        Assert.Equal(imageBytes, Assert.Single(page.Images!).ImageBytes);
    }

    /// <summary>
    /// 結合範囲の末端まで列定義と行定義が読み取られることを検証します。
    /// </summary>
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
        HeaderFooter? headerFooter = null,
        IReadOnlyList<ReportShape>? shapes = null) =>
        new(
            new ReportSheet(
                "Sheet1",
                cells ?? new Dictionary<CellAddress, ReportCell> { [new(1, 1)] = new(null, CellStyle.Default) },
                columns ?? new Dictionary<int, ColumnDefinition> { [1] = new() },
                rows ?? new Dictionary<int, RowDefinition> { [1] = new() },
                [],
                pageSettings ?? new(),
                Images: images,
                HeaderFooter: headerFooter,
                Shapes: shapes),
            new FixedTextMeasurer());

    private static byte[] CreateImageBytes()
    {
        using var bitmap = new SKBitmap(1, 1);
        bitmap.SetPixel(0, 0, SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static string CreateWorkbookWithPageSetup(
        bool fitToPage,
        uint scale,
        uint? fitToWidth,
        uint? fitToHeight)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("Sheet1");
            worksheet.Cell(1, 1).Value = "value";
            workbook.SaveAs(path);
        }

        using (var document = SpreadsheetDocument.Open(path, true))
        {
            var worksheetPart = document.WorkbookPart!.WorksheetParts.Single();
            var worksheet = worksheetPart.Worksheet;
            var properties = worksheet.SheetProperties ?? worksheet.InsertAt(new S.SheetProperties(), 0);
            properties.PageSetupProperties = new S.PageSetupProperties { FitToPage = fitToPage };
            var pageSetup = worksheet.GetFirstChild<S.PageSetup>();
            if (pageSetup is null)
            {
                pageSetup = worksheet.AppendChild(new S.PageSetup());
            }

            pageSetup.Scale = scale;
            pageSetup.FitToWidth = fitToWidth;
            pageSetup.FitToHeight = fitToHeight;
            worksheet.Save();
        }

        return path;
    }

    private sealed class FixedTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string text, FontStyle font, double availableWidth, bool wrap) => new(10, 10);
    }

    private sealed class MissingPrivateUseFontManager : IFontManager
    {
        private readonly ResolvedFont _font;

        public MissingPrivateUseFontManager()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf");
            _font = new("Noto Sans JP", 400, false, path)
            {
                FaceId = path,
                FontData = File.ReadAllBytes(path),
            };
        }

        public int ResolveTextRunsCallCount { get; private set; }

        public ResolvedFont Resolve(FontRequest request) => _font;

        public IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request)
        {
            ResolveTextRunsCallCount++;
            return [new("\uFFFD", _font) { SourceText = text, MissingPrivateUseGlyph = true }];
        }
    }

    private sealed class CountingFontResolver : IFontResolver
    {
        public int ResolveTypefaceCallCount { get; private set; }

        public byte[]? GetFont(string faceName) => null;

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            ResolveTypefaceCallCount++;
            return null;
        }
    }
}

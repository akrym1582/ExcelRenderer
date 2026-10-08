using ExcelRenderer.Abstractions;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using Xunit;

namespace ExcelRenderer.Tests;

/// <summary>シート座標、回転後の外接領域、繰り返しタイトルおよび本文クリップの位置計算を検証します。</summary>
public sealed class GeometryRegressionTests
{
    /// <summary>座標計算の比較で許容する絶対誤差 0.000001 ポイントです。</summary>
    private const double Tolerance = 1e-6;

    /// <summary>倍率と縦中央配置を変更しても、後続ページの繰り返しタイトル内で文字領域とセルの相対位置が保たれることを検証します。</summary>
    /// <param name="scale">ページ座標と寸法に適用する印刷倍率。</param>
    /// <param name="centered">ページ内容を縦方向の中央に配置するかどうか。</param>
    [Theory(DisplayName = "倍率と縦中央配置を変更しても、後続ページの繰り返しタイトル内で文字領域とセルの相対位置が保たれる")]
    [InlineData(1d, false)]
    [InlineData(0.8d, false)]
    [InlineData(0.8d, true)]
    public void Repeated_title_content_bounds_follow_rendered_cell(double scale, bool centered)
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("title", CellStyle.Default),
            [new(2, 1)] = new("a", CellStyle.Default),
            [new(3, 1)] = new("b", CellStyle.Default),
            [new(4, 1)] = new("c", CellStyle.Default),
        };
        var sheet = new ReportSheet(
            "S",
            cells,
            new Dictionary<int, ColumnDefinition> { [1] = new(50) },
            new Dictionary<int, RowDefinition> { [1] = new(20), [2] = new(20), [3] = new(20), [4] = new(20) },
            [],
            new(100, 70, 10, 10, 10, 10, Scale: scale, TitleRows: new(1, 1)) { VerticalCentered = centered },
            new(new(1, 1), new(4, 1)));
        var context = RunUpTo(sheet);
        var source = context.CellLayouts[new(1, 1)];

        var document = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet);

        Assert.True(document.Pages.Count >= 2);
        foreach (var page in document.Pages)
        {
            var title = page.Cells.Single(cell => cell.Cell.Text == "title");
            Assert.Equal(title.Bounds.Y + ((source.ContentBounds.Y - source.Bounds.Y) * scale), title.ContentBounds.Y, Tolerance);
            Assert.Equal(title.Bounds.X + ((source.ContentBounds.X - source.Bounds.X) * scale), title.ContentBounds.X, Tolerance);
            Assert.Equal(source.ContentBounds.Height * scale, title.ContentBounds.Height, Tolerance);
        }
    }

    /// <summary>印刷範囲を C:D 列に設定した場合、絶対アンカーと単一セルアンカーが同じページ座標へ配置されることを検証します。</summary>
    [Fact(DisplayName = "印刷範囲を C:D 列に設定した場合、絶対アンカーと単一セルアンカーが同じページ座標へ配置される")]
    public void Anchors_share_print_area_origin()
    {
        var absolute = new ReportImage(new(1, 1), 0, 0, 20, 20, [])
        {
            DrawingAnchor = new(DrawingAnchorKind.Absolute, null, 0, 0, null, 0, 0, 110, 5, 20, 20),
        };
        var oneCell = new ReportImage(new(1, 3), 10, 5, 20, 20, [])
        {
            DrawingAnchor = new(DrawingAnchorKind.OneCell, new(1, 3), 10, 5, null, 0, 0, 0, 0, 20, 20),
        };
        var outsideFrom = new ReportImage(new(1, 2), 40, 0, 40, 10, [])
        {
            DrawingAnchor = new(DrawingAnchorKind.OneCell, new(1, 2), 40, 0, null, 0, 0, 0, 0, 40, 10),
        };
        var sheet = CreateSheet(new(new(1, 3), new(2, 4)), absolute, oneCell, outsideFrom);

        var page = Assert.Single(new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet).Pages);

        Assert.Equal(3, page.Images!.Count);
        Assert.Equal(20, page.Images[0].Bounds.X, Tolerance);
        Assert.Equal(20, page.Images[1].Bounds.X, Tolerance);
        Assert.Equal(page.Images[0].Bounds.Y, page.Images[1].Bounds.Y, Tolerance);
        Assert.Equal(0, page.Images[2].Bounds.X, Tolerance);
        Assert.Equal(40, page.Images[2].Bounds.Width, Tolerance);
    }

    /// <summary>画像だけのシートで、画像全体を含む使用範囲とクリップ領域が自動的に設定されることを検証します。</summary>
    [Fact(DisplayName = "画像だけのシートで、画像全体を含む使用範囲とクリップ領域が自動的に設定される")]
    public void Image_only_sheet_keeps_whole_image_inside_clip()
    {
        var image = new ReportImage(new(1, 1), 0, 0, 30, 100, []);
        var sheet = CreateSheet(null, image);

        var page = Assert.Single(new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet).Pages);

        var renderImage = Assert.Single(page.Images!);
        var clip = renderImage.ClipBounds!.Value;
        Assert.True(clip.Y + clip.Height >= renderImage.Bounds.Y + renderImage.Bounds.Height - Tolerance);
        Assert.True(clip.X + clip.Width >= renderImage.Bounds.X + renderImage.Bounds.Width - Tolerance);
    }

    /// <summary>明示した印刷範囲から画像がはみ出しても範囲を広げず、画像を範囲内にクリップすることを検証します。</summary>
    [Fact(DisplayName = "明示した印刷範囲から画像がはみ出しても範囲を広げず、画像を範囲内にクリップする")]
    public void Explicit_print_area_is_not_widened_by_objects()
    {
        var image = new ReportImage(new(1, 1), 0, 0, 30, 100, []);
        var sheet = CreateSheet(new(new(1, 1), new(1, 1)), image);

        var page = Assert.Single(new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet).Pages);

        var renderImage = Assert.Single(page.Images!);
        Assert.Equal(15, renderImage.ClipBounds!.Value.Height, Tolerance);
    }

    /// <summary>二つのセルを結ぶアンカーの終端がセル境界に一致する場合、次のセルを使用範囲に含めないことを検証します。</summary>
    [Fact(DisplayName = "二つのセルを結ぶアンカーの終端がセル境界に一致する場合、次のセルを使用範囲に含めない")]
    public void TwoCell_end_on_boundary_is_half_open()
    {
        var image = new ReportImage(new(1, 1), 0, 0, 64, 15, [])
        {
            DrawingAnchor = new(DrawingAnchorKind.TwoCell, new(1, 1), 0, 0, new(2, 2), 0, 0, 0, 0, 0, 0),
        };
        var context = RunUpTo(CreateSheet(null, image), 2);

        Assert.Equal(new CellRange(new(1, 1), new(1, 1)), context.PrintArea);
    }

    /// <summary>倍率を変更しても、本文オブジェクトのクリップ領域から繰り返しタイトル行・列が除かれることを検証します。</summary>
    /// <param name="scale">ページ座標と寸法に適用する印刷倍率。</param>
    [Theory(DisplayName = "倍率を変更しても、本文オブジェクトのクリップ領域から繰り返しタイトル行・列が除かれる")]
    [InlineData(1d)]
    [InlineData(0.8d)]
    public void Body_clip_excludes_repeated_titles(double scale)
    {
        var cells = new Dictionary<CellAddress, ReportCell>();
        for (var row = 1; row <= 4; row++)
        {
            cells[new(row, 1)] = new("r" + row, CellStyle.Default);
            cells[new(row, 2)] = new("c" + row, CellStyle.Default);
        }

        var image = new ReportImage(new(2, 1), 0, 10, 40, 30, []);
        var sheet = new ReportSheet(
            "S",
            cells,
            new Dictionary<int, ColumnDefinition> { [1] = new(20), [2] = new(20) },
            new Dictionary<int, RowDefinition> { [1] = new(20), [2] = new(20), [3] = new(20), [4] = new(20) },
            [],
            new(100, 70, 10, 10, 10, 10, Scale: scale, TitleRows: new(1, 1)),
            new(new(1, 1), new(4, 2)),
            [image]);

        var pages = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet).Pages;

        Assert.True(pages.Count >= 2);
        Assert.Equal(10, pages[0].Images!.Single().ClipBounds!.Value.Y, Tolerance);
        foreach (var page in pages.Skip(1))
        {
            Assert.All(page.Images!, render => Assert.Equal(10 + (20 * scale), render.ClipBounds!.Value.Y, Tolerance));
        }
    }

    /// <summary>連続キャンバスの寸法に回転後の外接領域を含め、負の座標をキャンバス内へ移動することを検証します。</summary>
    [Fact(DisplayName = "連続キャンバスの寸法に回転後の外接領域を含め、負の座標をキャンバス内へ移動する")]
    public void Continuous_canvas_uses_rotated_visual_bounds()
    {
        var image = new ReportImage(new(1, 1), 0, 0, 100, 20, []) { Rotation = 90 };
        var sheet = CreateSheet(null, image);

        var result = new ReportLayoutEngine(new PdfSharpTextMeasurer()).LayoutContinuous(sheet);

        var render = Assert.Single(result.Document.Pages[0].Images!);
        Assert.Equal(100, result.Height, Tolerance);
        Assert.Equal(new ReportRect(0, 40, 100, 20), render.Bounds);
        Assert.Equal(60, result.Width, Tolerance);
    }

    /// <summary>45 度回転したオブジェクトの外接矩形が、中心を基準とした回転から求めた値と一致することを検証します。</summary>
    [Fact(DisplayName = "45 度回転したオブジェクトの外接矩形が、中心を基準とした回転から求めた値と一致する")]
    public void Visual_bounds_match_rotation_about_centre()
    {
        var bounds = ObjectGeometry.GetVisualBounds(new(0, 0, 100, 20), 45);

        var side = 120 / Math.Sqrt(2);
        Assert.Equal(side, bounds.Width, Tolerance);
        Assert.Equal(side, bounds.Height, Tolerance);
        Assert.Equal(50, bounds.X + (bounds.Width / 2), Tolerance);
        var rotated90 = ObjectGeometry.GetVisualBounds(new(0, 0, 100, 20), 90);
        Assert.Equal(100, rotated90.Height, Tolerance);
    }

    /// <summary>回転後の画像が隣のページに交差する場合、そのページの描画対象にも選ばれることを検証します。</summary>
    [Fact(DisplayName = "回転後の画像が隣のページに交差する場合、そのページの描画対象にも選ばれる")]
    public void Rotated_object_is_selected_by_visual_bounds()
    {
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("a", CellStyle.Default),
            [new(4, 1)] = new("b", CellStyle.Default),
        };
        var image = new ReportImage(new(1, 1), 0, 20, 100, 20, []) { Rotation = 90 };
        var sheet = new ReportSheet(
            "S",
            cells,
            new Dictionary<int, ColumnDefinition> { [1] = new(100) },
            new Dictionary<int, RowDefinition> { [1] = new(30), [2] = new(30), [3] = new(30), [4] = new(30) },
            [],
            new(200, 70, 0, 0, 0, 0, Scale: 1),
            new(new(1, 1), new(4, 1)),
            [image]);

        var pages = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet).Pages;

        Assert.True(pages.Count >= 2);
        Assert.True(pages[1].Images!.Count == 1);
    }

    /// <summary>印刷範囲と画像を指定して、位置計算の検証に使用するシートモデルを作成します。</summary>
    /// <param name="printArea">シートに設定する明示的な印刷範囲。</param>
    /// <param name="images">シートに配置する画像モデル。</param>
    private static ReportSheet CreateSheet(CellRange? printArea, params ReportImage[] images) => new(
        "S",
        new Dictionary<CellAddress, ReportCell>(),
        new Dictionary<int, ColumnDefinition> { [1] = new(50), [2] = new(50), [3] = new(50), [4] = new(50) },
        new Dictionary<int, RowDefinition>(),
        [],
        new(300, 300, 10, 10, 10, 10, Scale: 1),
        printArea,
        images);

    /// <summary>レイアウトパスを指定した位置まで順に実行し、途中の位置計算結果を返します。</summary>
    /// <param name="sheet">解析するシート名の期待値、または検証対象のシートモデル。</param>
    /// <param name="stop">実行を止めるレイアウトパスの位置。</param>
    private static ReportLayoutContext RunUpTo(ReportSheet sheet, int stop = int.MaxValue)
    {
        var context = new ReportLayoutContext(sheet, new PdfSharpTextMeasurer());
        IReportLayoutPass[] passes =
        [
            new NormalizePass(), new ResolvePrintAreaPass(), new HiddenRowColumnPass(),
            new ColumnLayoutPass(), new RowLayoutPass(), new TextMeasurePass(), new CellBoundsPass(),
        ];
        foreach (var pass in passes.Take(stop))
        {
            pass.Execute(context);
        }

        return context;
    }
}

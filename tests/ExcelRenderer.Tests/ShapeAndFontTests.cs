using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Fonts;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.SkiaSharp;
using SkiaSharp;
using Xunit;
using A = DocumentFormat.OpenXml.Drawing;
using S = DocumentFormat.OpenXml.Spreadsheet;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ExcelRenderer.Tests;

/// <summary>
/// DrawingML 図形の読み取り・描画とフォント解決を検証します。
/// </summary>
public sealed class ShapeAndFontTests
{
    /// <summary>
    /// 対応する DrawingML 図形の位置、形状、書式を読み取り、未知のジオメトリを無視することを検証します。
    /// </summary>
    [Fact]
    public void ExcelReader_reads_supported_DrawingML_shape_properties_and_skips_unknown_geometry()
    {
        var path = CreateWorkbookWithShapes();
        try
        {
            var sheet = Assert.Single(new ExcelReader().Read(path).Sheets);
            var shape = Assert.Single(sheet.Shapes!);

            Assert.Equal(ShapeKind.RoundedRectangle, shape.Kind);
            Assert.Equal(new CellAddress(1, 1), shape.Anchor);
            Assert.Equal(72, shape.Width, 3);
            Assert.Equal(36, shape.Height, 3);
            Assert.Equal(45, shape.Rotation, 3);
            Assert.Equal(new ReportColor(255, 0, 0), shape.Style.FillColor);
            Assert.Equal(new ReportColor(0, 0, 255), shape.Style.LineColor);
            Assert.Equal("日本語 ABC", shape.Text!.Text);
            Assert.True(shape.Text.Font.Bold);
            Assert.True(shape.Text.Font.Italic);
            Assert.Equal(HorizontalAlignment.Center, shape.Text.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, shape.Text.VerticalAlignment);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 画像と図形の描画命令が Z インデックス順に並ぶことを検証します。
    /// </summary>
    [Fact]
    public void DrawCommandGenerator_orders_images_and_shapes_by_ZIndex()
    {
        var shapeA = CreateShape(1, new ReportColor(255, 0, 0));
        var shapeB = CreateShape(3, new ReportColor(0, 255, 0));
        var page = new RenderPage(1, [], [new(new(0, 0, 10, 10), [1], 2)], Shapes:
            [new(new(0, 0, 10, 10), shapeB), new(new(0, 0, 10, 10), shapeA)]);

        var commands = new DrawCommandGeneratorPass().Generate(new RenderDocument([page]));

        Assert.IsType<DrawShapeCommand>(commands[0]);
        Assert.IsType<DrawImageCommand>(commands[1]);
        Assert.IsType<DrawShapeCommand>(commands[2]);
        Assert.Equal(3, ((DrawShapeCommand)commands[2]).Shape.ZIndex);
    }

    /// <summary>
    /// 図形の塗りつぶしと枠線が描画され、重なり順が維持されることを検証します。
    /// </summary>
    [Fact]
    public void PngRenderer_renders_shape_fill_border_and_Z_order()
    {
        var commands = new DrawCommand[]
        {
            new DrawShapeCommand(1, new(2, 2, 16, 16), CreateShape(0, new ReportColor(255, 0, 0))),
            new DrawShapeCommand(1, new(8, 8, 10, 10), CreateShape(1, new ReportColor(0, 255, 0))),
        };
        using var output = new MemoryStream();

        new PngRenderer().RenderPage(commands, new PageSettings(20, 20), output, 72);

        using var bitmap = SKBitmap.Decode(output.ToArray());
        Assert.Equal(SKColors.Red, bitmap.GetPixel(5, 5));
        Assert.Equal(new SKColor(0, 255, 0), bitmap.GetPixel(12, 12));
        Assert.Equal(SKColors.Blue, bitmap.GetPixel(2, 10));
    }

    /// <summary>
    /// 各対応図形について、回転と日本語テキストを含む描画が成功することを検証します。
    /// </summary>
    /// <param name="kind">描画を検証する図形の種類。</param>
    [Theory]
    [InlineData(ShapeKind.Rectangle)]
    [InlineData(ShapeKind.RoundedRectangle)]
    [InlineData(ShapeKind.Ellipse)]
    [InlineData(ShapeKind.WedgeRectangleCallout)]
    [InlineData(ShapeKind.WedgeRoundedRectangleCallout)]
    public void PngRenderer_renders_every_supported_geometry_with_rotation_and_Japanese_text(ShapeKind kind)
    {
        var text = new ShapeText(
            "日本語 ABC 123",
            new FontStyle(Size: 8, Bold: true, Italic: true),
            HorizontalAlignment.Center,
            VerticalAlignment.Center,
            true,
            2,
            2,
            2,
            2);
        var shape = CreateShape(0, new ReportColor(255, 255, 0)) with { Kind = kind, Rotation = 45, Text = text };
        using var output = new MemoryStream();

        new PngRenderer().RenderPage(
            [new DrawShapeCommand(1, new(5, 5, 30, 20), shape)],
            new PageSettings(40, 30),
            output,
            72);

        using var bitmap = SKBitmap.Decode(output.ToArray());
        Assert.NotNull(bitmap);
        Assert.Equal(40, bitmap.Width);
        Assert.Contains(
            Enumerable.Range(0, bitmap.Width).SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y))),
            color => color != SKColors.White);
    }

    /// <summary>
    /// 標準、太字、斜体、太字斜体の要求が登録済みの各フォントフェイスへ解決されることを検証します。
    /// </summary>
    [Fact]
    public void FontManager_resolves_regular_bold_italic_and_boldItalic_to_registered_faces()
    {
        var files = Enumerable.Range(0, 4).Select(_ => CopyTestFont()).ToArray();
        try
        {
            var manager = new FontManager(new FontOptions { FontDirectories = [] });
            manager.Register("Report Font", files[0], files[1], files[2], files[3]);

            Assert.Equal(files[0], manager.Resolve(new("Report Font", 400, false)).FilePath);
            Assert.Equal(files[1], manager.Resolve(new("Report Font", 700, false)).FilePath);
            Assert.Equal(files[2], manager.Resolve(new("Report Font", 400, true)).FilePath);
            Assert.Equal(files[3], manager.Resolve(new("Report Font", 700, true)).FilePath);
        }
        finally
        {
            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
    }

    /// <summary>
    /// 要求したファミリがない場合に設定済み代替ファミリと最も近いウェイトが選ばれることを検証します。
    /// </summary>
    [Fact]
    public void FontManager_uses_configured_family_fallback_and_nearest_weight()
    {
        var regular = CopyTestFont();
        var bold = CopyTestFont();
        try
        {
            var manager = new FontManager(new FontOptions { FontDirectories = [], FallbackFamilies = ["Noto Sans JP"] });
            manager.Register("Noto Sans JP", regular, bold);

            var resolved = manager.Resolve(new("Unknown Font", 600));

            Assert.Equal("Noto Sans JP", resolved.Family);
            Assert.Equal(700, resolved.Weight);
            Assert.Equal(bold, resolved.FilePath);
        }
        finally
        {
            File.Delete(regular);
            File.Delete(bold);
        }
    }

    /// <summary>
    /// ファイル名ではなくフォント内部のファミリ情報を使って登録されることを検証します。
    /// </summary>
    [Fact]
    public void FontManager_scans_family_metadata_instead_of_the_file_name()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "NotoSansCJKjp-Regular.otf"),
            Path.Combine(directory, "unrelated-file-name.otf"));
        try
        {
            var manager = new FontManager(new FontOptions { FontDirectories = [directory], FallbackFamilies = [] });

            var resolved = manager.Resolve(new("Noto Sans CJK JP"));

            Assert.Equal("Noto Sans CJK JP", resolved.Family);
            Assert.StartsWith(directory, resolved.FilePath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>同梱フォントだけでも決定的なフェイス ID と Unicode テキスト要素単位のランを返すことを検証します。</summary>
    [Fact]
    public void FontManager_resolves_bundled_font_and_keeps_combining_text_together()
    {
        var manager = new FontManager(new FontOptions
        {
            AllowSystemFonts = false,
            FontDirectories = [],
            FallbackFamilies = [],
        });

        var font = manager.Resolve(new("Noto Sans JP"));
        var runs = manager.ResolveTextRuns("A\u0301日本語", new("Noto Sans JP"));

        Assert.NotEmpty(font.FaceId);
        Assert.NotNull(font.FontData);
        Assert.NotEmpty(runs);
        Assert.Equal("A\u0301日本語", string.Concat(runs.Select(run => run.Text)));
        Assert.DoesNotContain(runs, run => run.Text == "\u0301");
    }

    /// <summary>IVS を基底文字から分離せず、format 14 に登録された同梱フォントで解決することを検証します。</summary>
    [Fact]
    public void FontManager_resolves_registered_ivs_as_one_text_element()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FontDirectories = [], FallbackFamilies = [] });

        var runs = manager.ResolveTextRuns("X\u4FAE\uFE00Y", new("Noto Sans JP"));

        Assert.Equal("X\u4FAE\uFE00Y", string.Concat(runs.Select(run => run.SourceText)));
        Assert.DoesNotContain(runs, run => run.MissingIvsGlyph);
        Assert.Contains(runs, run => run.SourceText.Contains("\u4FAE\uFE00", StringComparison.Ordinal));
    }

    /// <summary>IVS の優先書体をゴシック体と明朝体から明示的に選択できることを検証します。</summary>
    [Theory]
    [InlineData(IvsFontStyle.Gothic, "Sans")]
    [InlineData(IvsFontStyle.Mincho, "Serif")]
    public void FontManager_honors_selected_ivs_font_style(IvsFontStyle style, string expectedFamilyPart)
    {
        var manager = new FontManager(new FontOptions
        {
            AllowSystemFonts = false,
            FontDirectories = [],
            FallbackFamilies = [],
            IvsFontStyle = style,
        });

        var run = Assert.Single(manager.ResolveTextRuns("\u4FAE\uFE00", new("Noto Sans JP")));

        Assert.Contains(expectedFamilyPart, run.Font.Family, StringComparison.OrdinalIgnoreCase);
        Assert.False(run.MissingIvsGlyph);
    }

    /// <summary>未登録 IVS はセレクターを黙って捨てず、元列と UTF-16 位置を保持して欠字化することを検証します。</summary>
    [Fact]
    public void FontManager_marks_unsupported_supplementary_ivs_as_missing()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FontDirectories = [], FallbackFamilies = [] });
        var source = "A\u4E00\U000E01EFB";

        var missing = Assert.Single(manager.ResolveTextRuns(source, new("Noto Sans JP")), run => run.MissingIvsGlyph);

        Assert.Equal("\u4E00\U000E01EF", missing.SourceText);
        Assert.Equal("\uFFFD", missing.Text);
        Assert.Equal(1, missing.Utf16Start);
    }

    /// <summary>絵文字の標準化異体字シーケンスを IVS 欠字として置換しないことを検証します。</summary>
    [Fact]
    public void FontManager_preserves_non_ideographic_variation_sequences()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FontDirectories = [], FallbackFamilies = [] });
        const string source = "\u2764\uFE0F";

        var run = Assert.Single(manager.ResolveTextRuns(source, new("Noto Sans JP")));

        Assert.Equal(source, run.SourceText);
        Assert.Equal(source, run.Text);
        Assert.False(run.MissingIvsGlyph);
    }

    /// <summary>明示登録を優先するポリシーが同梱フォントではなく登録済みファイルを選ぶことを検証します。</summary>
    [Fact]
    public void FontManager_prefer_requested_uses_explicit_registration()
    {
        var path = CopyTestFont();
        try
        {
            var manager = new FontManager(new FontOptions
            {
                Policy = FontPolicy.PreferRequested,
                AllowSystemFonts = false,
                FontDirectories = [],
                FallbackFamilies = [],
                Registrations = [new("Noto Sans JP", path)],
            });

            var font = manager.Resolve(new("Noto Sans JP"));

            Assert.Equal(path, font.FilePath);
            Assert.True(font.FontStyleApproximated == false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ReportShape CreateShape(int z, ReportColor fill) => new(
        new(1, 1),
        0,
        0,
        10,
        10,
        ShapeKind.Rectangle,
        new(fill, new ReportColor(0, 0, 255), 2),
        null,
        0,
        z);

    private static string CopyTestFont()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.otf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "NotoSansCJKjp-Regular.otf"), path);
        return path;
    }

    private static string CreateWorkbookWithShapes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.xlsx");
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("Shapes").Cell("A1").Value = "anchor";
            workbook.SaveAs(path);
        }

        using var document = SpreadsheetDocument.Open(path, true);
        var worksheetPart = document.WorkbookPart!.WorksheetParts.Single();
        var drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
        var relationshipId = worksheetPart.GetIdOfPart(drawingsPart);
        worksheetPart.Worksheet.Append(new S.Drawing { Id = relationshipId });
        worksheetPart.Worksheet.Save();

        var known = new Xdr.TwoCellAnchor(
            Marker<Xdr.FromMarker>(0, 0),
            Marker<Xdr.ToMarker>(2, 2),
            CreateOpenXmlShape(1, A.ShapeTypeValues.RoundRectangle),
            new Xdr.ClientData());
        var unknown = new Xdr.TwoCellAnchor(
            Marker<Xdr.FromMarker>(0, 0),
            Marker<Xdr.ToMarker>(2, 2),
            CreateOpenXmlShape(2, A.ShapeTypeValues.Triangle),
            new Xdr.ClientData());
        drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing(known, unknown);
        drawingsPart.WorksheetDrawing.Save();
        return path;
    }

    private static T Marker<T>(int column, int row)
        where T : OpenXmlCompositeElement, new()
    {
        var marker = new T();
        marker.Append(
            new Xdr.ColumnId(column.ToString()),
            new Xdr.ColumnOffset("0"),
            new Xdr.RowId(row.ToString()),
            new Xdr.RowOffset("0"));
        return marker;
    }

    private static Xdr.Shape CreateOpenXmlShape(uint id, A.ShapeTypeValues geometry)
    {
        var properties = new Xdr.ShapeProperties(
            new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = 914400, Cy = 457200 }) { Rotation = 2700000 },
            new A.PresetGeometry { Preset = geometry },
            new A.SolidFill(new A.RgbColorModelHex { Val = "FF0000" }),
            new A.Outline(new A.SolidFill(new A.RgbColorModelHex { Val = "0000FF" })) { Width = 12700 });
        var body = new Xdr.TextBody(
            new A.BodyProperties { Anchor = A.TextAnchoringTypeValues.Center },
            new A.ListStyle(),
            new A.Paragraph(
            new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Center },
            new A.Run(new A.RunProperties { Bold = true, Italic = true, FontSize = 1200 }, new A.Text("日本語 ABC"))));
        return new Xdr.Shape(
            new Xdr.NonVisualShapeProperties(
            new Xdr.NonVisualDrawingProperties { Id = id, Name = $"Shape {id}" }, new Xdr.NonVisualShapeDrawingProperties()),
            properties,
            body);
    }
}

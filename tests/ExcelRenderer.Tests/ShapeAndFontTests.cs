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
    /// 埋め込みリソースがない場合に、ExcelRenderer.dll 配下の同名ファイルを読み込むことを検証します。
    /// </summary>
    [Fact(DisplayName = "埋め込みリソースがない場合に、ExcelRenderer.dll 配下の同名ファイルを読み込む")]
    public void OptionalFontPack_loads_missing_resource_from_ExcelRenderer_subdirectory()
    {
        var excelRendererDirectory = Path.GetDirectoryName(typeof(OptionalFontPack).Assembly.Location)!;
        var directory = Path.Combine(excelRendererDirectory, $"font-fallback-{Guid.NewGuid():N}");
        var name = $"fallback-{Guid.NewGuid():N}.ttf";
        var expected = new byte[] { 1, 2, 3, 4 };
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, name), expected);

            var actual = OptionalFontPack.LoadFontData(name);

            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// ExcelRenderer.dll と同じディレクトリにある DLL から埋め込みフォントを読み込み、
    /// 読み込めない DLL があっても探索を継続することを検証します。
    /// </summary>
    [Fact(DisplayName = "ExcelRenderer.dll と同じディレクトリにある DLL から埋め込みフォントを読み込み、 読み込めない DLL があっても探索を継続する")]
    public void OptionalFontPack_loads_first_embedded_font_from_neighboring_dll_and_ignores_invalid_dll()
    {
        var excelRendererDirectory = Path.GetDirectoryName(typeof(OptionalFontPack).Assembly.Location)!;
        var invalidDll = Path.Combine(excelRendererDirectory, $"000-invalid-{Guid.NewGuid():N}.dll");
        File.WriteAllText(invalidDll, "not a managed assembly");
        try
        {
            var actual = OptionalFontPack.LoadFontData("NotoColorEmoji.ttf");

            Assert.NotNull(actual);
            Assert.NotEmpty(actual);
        }
        finally
        {
            File.Delete(invalidDll);
        }
    }

    /// <summary>
    /// 対応する DrawingML 図形の位置、形状、書式を読み取り、未知のジオメトリを無視することを検証します。
    /// </summary>
    [Fact(DisplayName = "対応する DrawingML 図形の位置、形状、書式を読み取り、未知のジオメトリを無視する")]
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
    [Fact(DisplayName = "画像と図形の描画命令が Z インデックス順に並ぶ")]
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
    [Fact(DisplayName = "図形の塗りつぶしと枠線が描画され、重なり順が維持される")]
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
    [Theory(DisplayName = "各対応図形について、回転と日本語テキストを含む描画が成功する")]
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
    [Fact(DisplayName = "標準、太字、斜体、太字斜体の要求が登録済みの各フォントフェイスへ解決される")]
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
    /// 同じフォントデータを別名および別書体として登録しても、解決結果の登録属性が混同されないことを検証します。
    /// </summary>
    /// <param name="resolveBoldAliasFirst">太字の別名を先に解決してキャッシュの順序依存を検証するかどうか。</param>
    [Theory(DisplayName = "同じフォントデータを別名および別書体として登録しても、解決結果の登録属性が混同されない")]
    [InlineData(false)]
    [InlineData(true)]
    public void FontManager_keeps_alias_and_registered_style_in_resolved_cache(bool resolveBoldAliasFirst)
    {
        var path = CopyTestFont();
        try
        {
            var manager = new FontManager(new FontOptions
            {
                AllowSystemFonts = false,
                UseFontPack = false,
                FontDirectories = [],
                FallbackFamilies = [],
            });
            manager.Register("A", path);
            manager.Register("B", path, bold: path, italic: path);

            ResolvedFont a;
            ResolvedFont b;
            if (resolveBoldAliasFirst)
            {
                b = manager.Resolve(new("B", 700, false));
                a = manager.Resolve(new("A", 700, false));
            }
            else
            {
                a = manager.Resolve(new("A", 700, false));
                b = manager.Resolve(new("B", 700, false));
            }

            Assert.Equal(("A", 400, true), (a.Family, a.Weight, a.FontStyleApproximated));
            Assert.Equal(("B", 700, false), (b.Family, b.Weight, b.FontStyleApproximated));
            var italic = manager.Resolve(new FontRequest("B", 400, true));
            Assert.Equal(("B", 400, true, false),
                (italic.Family, italic.Weight, italic.Italic, italic.FontStyleApproximated));
            Assert.Same(b, manager.Resolve(new("B", 700, false)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>登録追加後に要求キャッシュを破棄し、新しく最適になった書体を選ぶことを検証します。</summary>
    [Fact(DisplayName = "登録追加後に要求キャッシュを破棄し、新しく最適になった書体を選ぶ")]
    public void FontManager_reselects_after_registration_changes_candidates()
    {
        var path = CopyTestFont();
        try
        {
            var manager = new FontManager(new FontOptions
            {
                AllowSystemFonts = false,
                UseFontPack = false,
                FontDirectories = [],
                FallbackFamilies = [],
            });
            manager.Register("A", path);
            Assert.True(manager.Resolve(new("A", 700, false)).FontStyleApproximated);

            manager.Register("A", path, bold: path);

            var resolved = manager.Resolve(new FontRequest("A", 700, false));
            Assert.Equal(700, resolved.Weight);
            Assert.False(resolved.FontStyleApproximated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 要求したファミリがない場合に設定済み代替ファミリと最も近いウェイトが選ばれることを検証します。
    /// </summary>
    [Fact(DisplayName = "要求したファミリがない場合に設定済み代替ファミリと最も近いウェイトが選ばれる")]
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
    [Fact(DisplayName = "ファイル名ではなくフォント内部のファミリ情報を使って登録される")]
    public void FontManager_scans_family_metadata_instead_of_the_file_name()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"),
            Path.Combine(directory, "unrelated-file-name.otf"));
        try
        {
            var manager = new FontManager(new FontOptions { FontDirectories = [directory], FallbackFamilies = [] });

            var resolved = manager.Resolve(new("Noto Sans JP"));

            Assert.Equal("Noto Sans JP", resolved.Family);
            Assert.StartsWith(directory, resolved.FilePath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>拡張子ではなく内容を検査し、TTE 名の外部ファイルを内部ファミリー名で登録します。</summary>
    [Fact(DisplayName = "拡張子ではなく内容を検査し、TTE 名の外部ファイルを内部ファミリー名で登録します")]
    public void FontManager_registers_explicit_tte_by_its_internal_metadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.tte");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"), path);
        try
        {
            var manager = new FontManager(new FontOptions
            {
                UseFontPack = false,
                AllowSystemFonts = false,
                FallbackFamilies = [],
                FontFiles = [path],
            });

            var resolved = manager.Resolve(new("Noto Sans JP"));

            Assert.Equal(path, resolved.FilePath);
            Assert.Equal("Noto Sans JP", resolved.Family);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>明示ファイルの欠落と不正な内容を入力エラーとして報告します。</summary>
    [Fact(DisplayName = "明示ファイルの欠落と不正な内容を入力エラーとして報告します")]
    public void FontManager_rejects_missing_or_invalid_explicit_font_files()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.tte");
        var invalid = Path.GetTempFileName();
        File.WriteAllText(invalid, "not a font");
        try
        {
            Assert.Contains("not found", Assert.Throws<ArgumentException>(() => new FontManager(new FontOptions
            {
                FontFiles = [missing],
            })).Message);
            Assert.Contains("not a supported", Assert.Throws<ArgumentException>(() => new FontManager(new FontOptions
            {
                FontFiles = [invalid],
            })).Message);
        }
        finally
        {
            File.Delete(invalid);
        }
    }

    /// <summary>外部ファイル指定時に未収録の BMP 私用文字を置換対象として保持します。</summary>
    [Fact(DisplayName = "外部ファイル指定時に未収録の BMP 私用文字を置換対象として保持します")]
    public void FontManager_marks_missing_private_use_glyph_when_external_fonts_are_configured()
    {
        var path = CopyTestFont();
        try
        {
            var manager = new FontManager(new FontOptions
            {
                AllowSystemFonts = false,
                FallbackFamilies = [],
                FontFiles = [path],
            });

            var run = Assert.Single(manager.ResolveTextRuns("\uE000", new("Noto Sans JP")));

            Assert.True(run.MissingPrivateUseGlyph);
            Assert.Equal("\uE000", run.SourceText);
            Assert.Equal("\uFFFD", run.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>同梱フォントだけでも決定的なフェイス ID と Unicode テキスト要素単位のランを返すことを検証します。</summary>
    [Fact(DisplayName = "同梱フォントだけでも決定的なフェイス ID と Unicode テキスト要素単位のランを返す")]
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
        Assert.Equal("Noto Sans JP", font.Family);
        Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf")), font.FontData);
        Assert.NotEmpty(runs);
        Assert.Equal("A\u0301日本語", string.Concat(runs.Select(run => run.Text)));
        Assert.DoesNotContain(runs, run => run.Text == "\u0301");
    }


    /// <summary>IVS を基底文字から分離せず、format 14 に登録された同梱フォントで解決することを検証します。</summary>
    [Fact(DisplayName = "IVS を基底文字から分離せず、format 14 に登録された同梱フォントで解決する")]
    public void FontManager_resolves_registered_ivs_as_one_text_element()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FontDirectories = [], FallbackFamilies = [] });

        var runs = manager.ResolveTextRuns("X\u4FAE\uFE00Y", new("Noto Sans JP"));

        Assert.Equal("X\u4FAE\uFE00Y", string.Concat(runs.Select(run => run.SourceText)));
        Assert.DoesNotContain(runs, run => run.MissingIvsGlyph);
        Assert.Contains(runs, run => run.SourceText.Contains("\u4FAE\uFE00", StringComparison.Ordinal));
    }

    /// <summary>IVS を隣接する通常文字から分離し、選択したフォント固有の glyph ID を保持することを検証します。</summary>
    [Fact(DisplayName = "IVS を隣接する通常文字から分離し、選択したフォント固有の glyph ID を保持する")]
    public void FontManager_keeps_resolved_ivs_glyph_separate_from_neighbors()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FontDirectories = [], FallbackFamilies = [] });

        var runs = manager.ResolveTextRuns("A\u4FAE\uFE00B", new("Noto Sans JP"));
        var ivs = Assert.Single(runs, run => run.GlyphId is not null);

        Assert.Equal("\u4FAE\uFE00", ivs.SourceText);
        Assert.NotEqual((ushort)0, ivs.GlyphId);
        Assert.Equal("A\u4FAE\uFE00B", string.Concat(runs.Select(run => run.SourceText)));
        Assert.Equal(3, runs.Count);
    }

    /// <summary>同梱ゴシック体がない場合は、どちらの設定でも IPAmj 明朝で IVS を解決することを検証します。</summary>
    /// <param name="style">フォントの解決に使用する書体設定。</param>
    /// <param name="text">計測・描画または解析の対象となる文字列。</param>
    /// <param name="expectedFamilyPart">解決後のファミリ名に含まれる文字列の期待値。</param>
    [Theory(DisplayName = "同梱ゴシック体がない場合は、どちらの設定でも IPAmj 明朝で IVS を解決する")]
    [InlineData(IvsFontStyle.Gothic, "\u4FAE\uFE00", "IPAmjMincho")]
    [InlineData(IvsFontStyle.Mincho, "\u4FAE\uFE00", "IPAmjMincho")]
    public void FontManager_honors_selected_ivs_font_style(IvsFontStyle style, string text, string expectedFamilyPart)
    {
        var manager = new FontManager(new FontOptions
        {
            AllowSystemFonts = false,
            FontDirectories = [],
            FallbackFamilies = [],
            IvsFontStyle = style,
        });

        var run = Assert.Single(manager.ResolveTextRuns(text, new("Noto Sans JP")));

        Assert.Contains(expectedFamilyPart, run.Font.Family, StringComparison.OrdinalIgnoreCase);
        Assert.False(run.MissingIvsGlyph);
    }

    /// <summary>IVS 置換を有効にすると異体字セレクターを除き、基底文字のみを描画することを検証します。</summary>
    /// <param name="source">比較元の画像、または読み取り元のストリーム。</param>
    /// <param name="expected">判定または数値比較の期待値。</param>
    [Theory(DisplayName = "IVS 置換を有効にすると異体字セレクターを除き、基底文字のみを描画する")]
    [InlineData("A\u4FAE\uFE00B", "A\u4FAEB")]
    [InlineData("A\U00020000\U000E0100B", "A\U00020000B")]
    public void FontManager_can_replace_ivs_with_its_base_character(string source, string expected)
    {
        var manager = new FontManager(new FontOptions
        {
            AllowSystemFonts = false,
            FontDirectories = [],
            FallbackFamilies = [],
            ReplaceIvsWithBaseCharacter = true,
        });

        var runs = manager.ResolveTextRuns(source, new("Noto Sans JP"));

        Assert.Equal(source, string.Concat(runs.Select(run => run.SourceText)));
        Assert.Equal(expected, string.Concat(runs.Select(run => run.Text)));
        Assert.DoesNotContain(runs, run => run.MissingIvsGlyph);
    }

    /// <summary>未登録 IVS はセレクターを黙って捨てず、元列と UTF-16 位置を保持して欠字化することを検証します。</summary>
    [Fact(DisplayName = "未登録 IVS はセレクターを黙って捨てず、元列と UTF-16 位置を保持して欠字化する")]
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
    [Fact(DisplayName = "絵文字の標準化異体字シーケンスを IVS 欠字として置換しない")]
    public void FontManager_preserves_non_ideographic_variation_sequences()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FontDirectories = [], FallbackFamilies = [] });
        const string source = "\u2764\uFE0F";

        var run = Assert.Single(manager.ResolveTextRuns(source, new("Noto Sans JP")));

        Assert.Equal(source, run.SourceText);
        Assert.Equal(source, run.Text);
        Assert.False(run.MissingIvsGlyph);
    }

    /// <summary>通常の絵文字と VS16 付き絵文字を同梱カラー書体に解決します。</summary>
    /// <param name="text">計測・描画または解析の対象となる文字列。</param>
    [Theory(DisplayName = "通常の絵文字と VS16 付き絵文字を同梱カラー書体に解決します")]
    [InlineData("A😀B")]
    [InlineData("A❤️B")]
    public void FontManager_uses_color_emoji_font_for_simple_emoji(string text)
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FallbackFamilies = [] });
        var runs = manager.ResolveTextRuns(text, new("Noto Sans JP"));
        var emoji = Assert.Single(runs, run => run.ColorEmojiGlyphId is not null);

        Assert.Equal("Noto Color Emoji", emoji.Font.Family);
        Assert.Equal(text, string.Concat(runs.Select(run => run.SourceText)));
    }

    /// <summary>絵文字を日本語フォントの欠字ではなくカラー画素として PNG に描画します。</summary>
    [Fact(DisplayName = "絵文字を日本語フォントの欠字ではなくカラー画素として PNG に描画します")]
    public void PngRenderer_draws_color_emoji_from_optional_font()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FallbackFamilies = [] });
        using var output = new MemoryStream();
        new PngRenderer(manager).RenderPage(
            [new DrawTextCommand(1, new(4, 4, 80, 58), "😀", CellStyle.Default with
            { Font = new FontStyle("Noto Sans JP", 40) })],
            new PageSettings(88, 66), output, 72);

        using var bitmap = SKBitmap.Decode(output.ToArray());
        Assert.Contains(Enumerable.Range(0, bitmap.Width).SelectMany(x => Enumerable.Range(0, bitmap.Height)
            .Select(y => bitmap.GetPixel(x, y))), color => color.Red > 180 && color.Green > 80 && color.Blue < 100);
    }

    /// <summary>フォントパッケージを無効にした場合、システムフォントなしでは解決できません。</summary>
    [Fact(DisplayName = "フォントパッケージを無効にした場合、システムフォントなしでは解決できません")]
    public void FontManager_without_font_pack_does_not_use_embedded_fonts()
    {
        var manager = new FontManager(new FontOptions
        {
            UseFontPack = false,
            AllowSystemFonts = false,
            FallbackFamilies = [],
        });

        Assert.Throws<InvalidOperationException>(() => manager.Resolve(new("Noto Sans JP")));
    }

    /// <summary>IVS 用の明朝体を任意パッケージから解決します。</summary>
    [Fact(DisplayName = "IVS 用の明朝体を任意パッケージから解決します")]
    public void FontManager_resolves_optional_mincho_font()
    {
        var manager = new FontManager(new FontOptions { AllowSystemFonts = false, FallbackFamilies = [] });

        Assert.NotEmpty(manager.Resolve(new("IPAmjMincho")).FontData!);
    }

    /// <summary>明示登録を優先するポリシーが同梱フォントではなく登録済みファイルを選ぶことを検証します。</summary>
    [Fact(DisplayName = "明示登録を優先するポリシーが同梱フォントではなく登録済みファイルを選ぶ")]
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

    /// <summary>指定した重なり順と塗りつぶし色を持つ、図形描画用のモデルを作成します。</summary>
    /// <param name="z">画像・図形の重なり順。</param>
    /// <param name="fill">図形の塗りつぶし色。</param>
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

    /// <summary>フォント登録の検証用に、同梱フォントを一時ファイルへコピーします。</summary>
    private static string CopyTestFont()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.otf");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "NotoSansJP-Regular.ttf"), path);
        return path;
    }

    /// <summary>対応する図形と未知の図形を含む DrawingML を持つ一時ブックを作成します。</summary>
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

    /// <summary>指定した行列番号から DrawingML のアンカー始点または終点を構築します。</summary>
    /// <param name="column">アンカーの列番号（0 始まり）。</param>
    /// <param name="row">アンカーの行番号（0 始まり）。</param>
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

    /// <summary>識別子と図形の種類を指定して、読み取り検証用の DrawingML 図形を作成します。</summary>
    /// <param name="id">DrawingML 図形の識別番号。</param>
    /// <param name="geometry">DrawingML に設定する図形の種類。</param>
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

using ClosedXML.Excel;
using PdfSharp.Fonts;
using ExcelRenderer.Drawing;
using ExcelRenderer.Excel;
using ExcelRenderer.Layout;
using ExcelRenderer.Model;
using ExcelRenderer.PdfSharp;
using SkiaSharp;
using Xunit;

namespace ExcelRenderer.Tests;

public sealed class PdfSampleOutputTests
{
    private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "PdfSamples");

    [Fact]
    public void Generates_pdf_samples_for_visual_inspection()
    {
        ConfigureJapaneseFont();
        Directory.CreateDirectory(OutputDirectory);

        var japanese = WriteSample("01-japanese.pdf", CreateJapaneseSheet());
        var images = WriteSample("02-image.pdf", CreateImageSheet());
        var wrappedText = WriteSample("03-wrapped-text.pdf", CreateWrappedTextSheet());
        var textDecoration = WriteSample("04-text-decoration.pdf", CreateTextDecorationSheet());

        Assert.Equal("日本語 PDF 出力サンプル", japanese.Sheet.Cells[new(1, 1)].Text);
        Assert.Equal(2, images.Sheet.Images!.Count);
        Assert.Equal(2, images.Commands.OfType<DrawImageCommand>().Count());
        Assert.True(wrappedText.Sheet.Cells[new(2, 1)].Style.WrapText);
        Assert.NotNull(wrappedText.Sheet.Cells[new(2, 1)].Style.Background);
        Assert.True(textDecoration.Sheet.Cells[new(3, 1)].Style.Font.Bold);
        Assert.True(textDecoration.Sheet.Cells[new(3, 2)].Style.Font.Italic);
        Assert.True(textDecoration.Sheet.Cells[new(3, 3)].Style.Font.Underline);
        Assert.Equal(HorizontalAlignment.Right,
            textDecoration.Sheet.Cells[new(4, 3)].Style.HorizontalAlignment);
    }

    [Fact]
    public void Generates_border_sample_from_excel()
    {
        ConfigureJapaneseFont();
        Directory.CreateDirectory(OutputDirectory);

        var sample = WriteSample("05-borders.pdf", CreateBorderSheet());
        var borderCommands = sample.Commands.OfType<DrawBorderCommand>().ToArray();

        Assert.Equal(sample.Sheet.Cells.Count(cell => cell.Value.Style.Border is not null), borderCommands.Length);

        var mixedBorder = sample.Sheet.Cells[new(3, 4)].Style.Border!;
        Assert.Equal(new BorderSide(2, new(220, 60, 60, 255)), mixedBorder.Left);
        Assert.Equal(new BorderSide(1, new(50, 100, 200, 255)), mixedBorder.Top);
        Assert.Equal(new BorderSide(0.5, new(40, 150, 90, 255)), mixedBorder.Right);
        Assert.Equal(new BorderSide(2, new(140, 70, 180, 255)), mixedBorder.Bottom);

        var topOnly = sample.Sheet.Cells[new(5, 1)].Style.Border!;
        Assert.NotNull(topOnly.Top);
        Assert.Null(topOnly.Left);
        Assert.Null(topOnly.Right);
        Assert.Null(topOnly.Bottom);
    }

    [Fact]
    public void Generates_layout_sample_covering_readme_features()
    {
        ConfigureJapaneseFont();
        Directory.CreateDirectory(OutputDirectory);

        var sample = WriteSample("06-layout-and-pagination.pdf", CreateLayoutAndPaginationSheet());
        var textCommands = sample.Commands.OfType<DrawTextCommand>().ToArray();

        Assert.Equal(new CellRange(new(1, 1), new(10, 5)), sample.Sheet.PrintArea);
        Assert.Equal(595.28, sample.Sheet.PageSettings.Width, 2);
        Assert.Equal(419.53, sample.Sheet.PageSettings.Height, 2);
        Assert.Equal(30, sample.Sheet.PageSettings.MarginLeft, 2);
        Assert.True(sample.Sheet.Columns[2].IsHidden);
        Assert.True(sample.Sheet.Rows[4].IsHidden);
        Assert.Equal(3, sample.Sheet.Cells[new(1, 1)].ColumnSpan);
        Assert.Equal(4, sample.Layout.Pages.Count);
        Assert.DoesNotContain(textCommands, command => command.Text.Contains("非表示列", StringComparison.Ordinal));
        Assert.DoesNotContain(textCommands, command => command.Text.Contains("非表示行", StringComparison.Ordinal));
        Assert.DoesNotContain(textCommands, command => command.Text.Contains("印刷範囲外", StringComparison.Ordinal));
        Assert.Contains(textCommands, command => command.Text == "ページ 1 / 4");
        Assert.Contains(textCommands, command => command.Text == "ページ 4 / 4");
    }

    [Fact]
    public void Excel_reader_preserves_multiple_sheets_for_caller_selection()
    {
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, "07-multiple-sheets.xlsx");
        using (var workbook = new XLWorkbook())
        {
            workbook.AddWorksheet("売上").Cell(1, 1).Value = "売上シート";
            workbook.AddWorksheet("在庫").Cell(1, 1).Value = "在庫シート";
            workbook.SaveAs(path);
        }

        var document = new ExcelReader().Read(path);

        Assert.Equal(["売上", "在庫"], document.Sheets.Select(sheet => sheet.Name));
        Assert.Equal("在庫シート", document.Sheets[1].Cells[new(1, 1)].Text);
    }

    private static SampleOutput WriteSample(string fileName, ReportSheet sourceSheet)
    {
        var excelPath = WriteExcelSample(Path.ChangeExtension(fileName, ".xlsx"), sourceSheet);
        var sheet = Assert.Single(new ExcelReader().Read(excelPath).Sheets);

        var layout = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet);
        var commands = new DrawCommandGeneratorPass().Generate(layout);
        var path = Path.Combine(OutputDirectory, fileName);

        using (var output = File.Create(path))
            new PdfSharpRenderer().Render(commands, sheet.PageSettings, output);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0, $"PDF が出力されていません: {path}");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

        return new(sheet, layout, commands, path);
    }

    private static string WriteExcelSample(string fileName, ReportSheet sheet)
    {
        var path = Path.Combine(OutputDirectory, fileName);
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet(sheet.Name);

        ApplyPageSettings(worksheet, sheet);

        foreach (var (columnNumber, column) in sheet.Columns)
        {
            var target = worksheet.Column(columnNumber);
            target.Width = (column.Width * 96d / 72d - 5) / 7;
            if (column.IsHidden)
                target.Hide();
        }

        foreach (var (rowNumber, row) in sheet.Rows)
        {
            var target = worksheet.Row(rowNumber);
            target.Height = row.Height;
            if (row.IsHidden)
                target.Hide();
        }

        foreach (var (address, reportCell) in sheet.Cells)
        {
            var cell = worksheet.Cell(address.Row, address.Column);
            cell.Value = reportCell.Text ?? string.Empty;
            ApplyStyle(cell, reportCell.Style);

            if (reportCell.RowSpan > 1 || reportCell.ColumnSpan > 1)
                worksheet.Range(address.Row, address.Column,
                    address.Row + reportCell.RowSpan - 1, address.Column + reportCell.ColumnSpan - 1).Merge();
        }

        foreach (var (image, index) in (sheet.Images ?? []).Select((image, index) => (image, index)))
        {
            using var stream = new MemoryStream(image.ImageBytes);
            worksheet.AddPicture(stream, $"image-{index + 1}")!
                .MoveTo(worksheet.Cell(image.Anchor.Row, image.Anchor.Column),
                    (int)Math.Round(image.OffsetX * 96d / 72d), (int)Math.Round(image.OffsetY * 96d / 72d))
                .WithSize((int)Math.Round(image.Width * 96d / 72d), (int)Math.Round(image.Height * 96d / 72d));
        }

        workbook.SaveAs(path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0, $"Excel が出力されていません: {path}");
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(bytes, 0, 2));

        return path;
    }

    private static void ApplyStyle(IXLCell cell, CellStyle style)
    {
        cell.Style.Font.FontName = style.Font.Family;
        cell.Style.Font.FontSize = style.Font.Size;
        cell.Style.Font.Bold = style.Font.Bold;
        cell.Style.Font.Italic = style.Font.Italic;
        cell.Style.Font.Underline = style.Font.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;
        cell.Style.Font.FontColor = style.Font.Color is { } fontColor ? ToColor(fontColor) : XLColor.Black;
        if (style.Background is { } background)
            cell.Style.Fill.BackgroundColor = ToColor(background);
        cell.Style.Alignment.Horizontal = style.HorizontalAlignment switch
        {
            HorizontalAlignment.Center => XLAlignmentHorizontalValues.Center,
            HorizontalAlignment.Right => XLAlignmentHorizontalValues.Right,
            _ => XLAlignmentHorizontalValues.Left
        };
        cell.Style.Alignment.Vertical = style.VerticalAlignment switch
        {
            VerticalAlignment.Center => XLAlignmentVerticalValues.Center,
            VerticalAlignment.Bottom => XLAlignmentVerticalValues.Bottom,
            _ => XLAlignmentVerticalValues.Top
        };
        cell.Style.Alignment.WrapText = style.WrapText;
        cell.Style.Alignment.ShrinkToFit = style.ShrinkToFit;

        ApplyBorderSide(style.Border?.Left,
            value => cell.Style.Border.LeftBorder = value,
            value => cell.Style.Border.LeftBorderColor = value);
        ApplyBorderSide(style.Border?.Top,
            value => cell.Style.Border.TopBorder = value,
            value => cell.Style.Border.TopBorderColor = value);
        ApplyBorderSide(style.Border?.Right,
            value => cell.Style.Border.RightBorder = value,
            value => cell.Style.Border.RightBorderColor = value);
        ApplyBorderSide(style.Border?.Bottom,
            value => cell.Style.Border.BottomBorder = value,
            value => cell.Style.Border.BottomBorderColor = value);
    }

    private static void ApplyBorderSide(
        BorderSide? side,
        Action<XLBorderStyleValues> setStyle,
        Action<XLColor> setColor)
    {
        if (side is null)
            return;

        setStyle(side.Width >= 1.5
            ? XLBorderStyleValues.Thick
            : side.Width >= 0.75
                ? XLBorderStyleValues.Medium
                : XLBorderStyleValues.Thin);
        if (side.Color is { } color)
            setColor(ToColor(color));
    }

    private static void ApplyPageSettings(IXLWorksheet worksheet, ReportSheet sheet)
    {
        var settings = sheet.PageSettings;
        worksheet.PageSetup.PaperSize = ToPaperSize(settings);
        worksheet.PageSetup.PageOrientation = settings.Width > settings.Height
            ? XLPageOrientation.Landscape
            : XLPageOrientation.Portrait;
        worksheet.PageSetup.Margins.Left = settings.MarginLeft / 72d;
        worksheet.PageSetup.Margins.Top = settings.MarginTop / 72d;
        worksheet.PageSetup.Margins.Right = settings.MarginRight / 72d;
        worksheet.PageSetup.Margins.Bottom = settings.MarginBottom / 72d;

        if (sheet.PrintArea is { } printArea)
            worksheet.PageSetup.PrintAreas.Add(printArea.First.Row, printArea.First.Column,
                printArea.Last.Row, printArea.Last.Column);

        if (sheet.HeaderFooter is not { } headerFooter)
            return;

        if (!string.IsNullOrEmpty(headerFooter.Header.Left))
            worksheet.PageSetup.Header.Left.AddText(headerFooter.Header.Left);
        if (!string.IsNullOrEmpty(headerFooter.Header.Center))
            worksheet.PageSetup.Header.Center.AddText(headerFooter.Header.Center);
        if (!string.IsNullOrEmpty(headerFooter.Header.Right))
            worksheet.PageSetup.Header.Right.AddText(headerFooter.Header.Right);
        if (!string.IsNullOrEmpty(headerFooter.Footer.Left))
            worksheet.PageSetup.Footer.Left.AddText(headerFooter.Footer.Left);
        if (!string.IsNullOrEmpty(headerFooter.Footer.Center))
            worksheet.PageSetup.Footer.Center.AddText(headerFooter.Footer.Center);
        if (!string.IsNullOrEmpty(headerFooter.Footer.Right))
            worksheet.PageSetup.Footer.Right.AddText(headerFooter.Footer.Right);
    }

    private static XLPaperSize ToPaperSize(PageSettings settings)
    {
        var shortSide = Math.Min(settings.Width, settings.Height);
        var longSide = Math.Max(settings.Width, settings.Height);
        if (Math.Abs(shortSide - 419.53) < 1 && Math.Abs(longSide - 595.28) < 1)
            return XLPaperSize.A5Paper;
        if (Math.Abs(shortSide - 595.28) < 1 && Math.Abs(longSide - 841.89) < 1)
            return XLPaperSize.A4Paper;

        throw new ArgumentOutOfRangeException(nameof(settings),
            $"サンプル出力で未対応の用紙サイズです: {settings.Width} x {settings.Height}");
    }

    private static XLColor ToColor(ReportColor? color) => color is { } value
        ? XLColor.FromArgb(value.Alpha, value.Red, value.Green, value.Blue)
        : XLColor.NoColor;

    private static ReportSheet CreateJapaneseSheet()
    {
        var title = CellStyle.Default with
        {
            Font = new("Noto Sans JP", 18, Bold: true, Color: new(255, 255, 255)),
            Background = new(30, 75, 120),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var body = CellStyle.Default with { Font = new("Noto Sans JP", 12) };
        return CreateSheet("日本語", new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("日本語 PDF 出力サンプル", title, ColumnSpan: 3),
            [new(3, 1)] = new("氏名", body),
            [new(3, 2)] = new("山田 太郎", body),
            [new(4, 1)] = new("内容", body),
            [new(4, 2)] = new("漢字、ひらがな、カタカナ、英数字を含む本文です。", body, ColumnSpan: 2)
        }, new Dictionary<int, ColumnDefinition> { [1] = new(100), [2] = new(180), [3] = new(180) },
            new Dictionary<int, RowDefinition> { [1] = new(36), [2] = new(16), [3] = new(28), [4] = new(28) });
    }

    private static ReportSheet CreateImageSheet()
    {
        var style = CellStyle.Default with { Font = new("Noto Sans JP", 14, Bold: true) };
        return CreateSheet("画像", new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("画像描画サンプル", style, ColumnSpan: 2),
            [new(2, 1)] = new("PNG", CellStyle.Default with { HorizontalAlignment = HorizontalAlignment.Center }),
            [new(2, 2)] = new("JPEG", CellStyle.Default with { HorizontalAlignment = HorizontalAlignment.Center })
        }, new Dictionary<int, ColumnDefinition> { [1] = new(220), [2] = new(220) },
            new Dictionary<int, RowDefinition> { [1] = new(30), [2] = new(28), [3] = new(150) },
            [
                new(new(3, 1), 5, 5, 200, 110, CreateImageBytes(SKEncodedImageFormat.Png)),
                new(new(3, 2), 5, 5, 200, 110, CreateImageBytes(SKEncodedImageFormat.Jpeg))
            ]);
    }

    private static ReportSheet CreateWrappedTextSheet()
    {
        var style = CellStyle.Default with
        {
            Font = new("Noto Sans JP", 12),
            WrapText = true,
            Background = new(240, 248, 255),
            Border = new(new BorderSide(1, new(50, 100, 150)))
        };
        return CreateSheet("折り返し", new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("文字折り返しサンプル", CellStyle.Default with { Font = new("Noto Sans JP", 14, Bold: true) }),
            [new(2, 1)] = new("幅の狭いセル内で日本語の文章を折り返して表示します。改行も含めて確認できます。\n2 行目は明示的な改行です。", style)
        }, new Dictionary<int, ColumnDefinition> { [1] = new(220) },
            new Dictionary<int, RowDefinition> { [1] = new(30), [2] = new(120) });
    }

    private static ReportSheet CreateTextDecorationSheet()
    {
        var border = new BorderStyle(new(1, new(40, 40, 40)), new(1, new(40, 40, 40)), new(1, new(40, 40, 40)), new(1, new(40, 40, 40)));
        return CreateSheet("文字装飾", new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("文字装飾サンプル", CellStyle.Default with { Font = new("Noto Sans JP", 16, Bold: true), Background = new(255, 235, 180) }),
            [new(3, 1)] = new("太字", CellStyle.Default with { Font = new("Noto Sans JP", 13, Bold: true), Border = border }),
            [new(3, 2)] = new("斜体", CellStyle.Default with { Font = new("Noto Sans JP", 13, Italic: true), Border = border }),
            [new(3, 3)] = new("下線", CellStyle.Default with { Font = new("Noto Sans JP", 13, Underline: true), Border = border }),
            [new(4, 1)] = new("左揃え", CellStyle.Default with { Border = border }),
            [new(4, 2)] = new("中央揃え", CellStyle.Default with { Border = border, HorizontalAlignment = HorizontalAlignment.Center }),
            [new(4, 3)] = new("右揃え", CellStyle.Default with { Border = border, HorizontalAlignment = HorizontalAlignment.Right })
        }, new Dictionary<int, ColumnDefinition> { [1] = new(140), [2] = new(140), [3] = new(140) },
            new Dictionary<int, RowDefinition> { [1] = new(34), [2] = new(16), [3] = new(32), [4] = new(32) });
    }

    private static ReportSheet CreateBorderSheet()
    {
        var thin = UniformBorder(0.5, new(40, 40, 40));
        var medium = UniformBorder(1, new(50, 100, 200));
        var thick = UniformBorder(2, new(220, 60, 60));
        var mixed = new BorderStyle(
            Left: new(2, new(220, 60, 60)),
            Top: new(1, new(50, 100, 200)),
            Right: new(0.5, new(40, 150, 90)),
            Bottom: new(2, new(140, 70, 180)));
        var label = CellStyle.Default with
        {
            Font = new("Noto Sans JP", 11),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new(250, 250, 245)
        };

        return CreateSheet("罫線", new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("Excel 罫線のPDF描画", label with
            {
                Font = new("Noto Sans JP", 16, Bold: true),
                Background = new(225, 235, 250)
            }, ColumnSpan: 4),
            [new(3, 1)] = new("細線（黒）", label with { Border = thin }),
            [new(3, 2)] = new("中線（青）", label with { Border = medium }),
            [new(3, 3)] = new("太線（赤）", label with { Border = thick }),
            [new(3, 4)] = new("辺ごとの色と太さ", label with { Border = mixed }),
            [new(5, 1)] = new("上のみ", label with { Border = new(Top: new(1, new(50, 100, 200))) }),
            [new(5, 2)] = new("右のみ", label with { Border = new(Right: new(2, new(220, 60, 60))) }),
            [new(5, 3)] = new("下のみ", label with { Border = new(Bottom: new(0.5, new(40, 150, 90))) }),
            [new(5, 4)] = new("左のみ", label with { Border = new(Left: new(1, new(140, 70, 180))) })
        },
            new Dictionary<int, ColumnDefinition> { [1] = new(120), [2] = new(120), [3] = new(120), [4] = new(140) },
            new Dictionary<int, RowDefinition> { [1] = new(38), [2] = new(18), [3] = new(48), [4] = new(28), [5] = new(48) });
    }

    private static ReportSheet CreateLayoutAndPaginationSheet()
    {
        var grid = UniformBorder(0.5, new(90, 100, 110));
        var body = CellStyle.Default with
        {
            Border = grid,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var cells = new Dictionary<CellAddress, ReportCell>
        {
            [new(1, 1)] = new("結合セル（A1:C1）", body with
            {
                Font = new("Noto Sans JP", 15, Bold: true, Color: new(255, 255, 255)),
                Background = new(45, 95, 145)
            }, ColumnSpan: 3),
            [new(1, 5)] = new("横方向 2ページ目", body with { Background = new(255, 240, 205) }),
            [new(1, 6)] = new("印刷範囲外", body)
        };
        for (var row = 2; row <= 10; row++)
        {
            for (var column = 1; column <= 5; column++)
                cells[new(row, column)] = new($"R{row}C{column}", body);
        }
        cells[new(3, 2)] = new("非表示列", body);
        cells[new(4, 1)] = new("非表示行", body);
        cells[new(9, 5)] = new("縦方向 2ページ目", body with { Background = new(225, 245, 225) });

        return CreateSheet("ページ設定", cells,
            new Dictionary<int, ColumnDefinition>
            {
                [1] = new(150),
                [2] = new(90, IsHidden: true),
                [3] = new(150),
                [4] = new(150),
                [5] = new(150),
                [6] = new(120)
            },
            new Dictionary<int, RowDefinition>
            {
                [1] = new(42),
                [2] = new(44),
                [3] = new(44),
                [4] = new(44, IsHidden: true),
                [5] = new(44),
                [6] = new(44),
                [7] = new(44),
                [8] = new(44),
                [9] = new(44),
                [10] = new(44)
            },
            pageSettings: new(595.28, 419.53, 30, 36, 30, 36),
            printArea: new(new(1, 1), new(10, 5)));
    }

    private static BorderStyle UniformBorder(double width, ReportColor color)
    {
        var side = new BorderSide(width, color);
        return new(side, side, side, side);
    }

    private static ReportSheet CreateSheet(
        string name,
        IReadOnlyDictionary<CellAddress, ReportCell> cells,
        IReadOnlyDictionary<int, ColumnDefinition> columns,
        IReadOnlyDictionary<int, RowDefinition> rows,
        IReadOnlyList<ReportImage>? images = null,
        PageSettings? pageSettings = null,
        CellRange? printArea = null) =>
        new(name, cells, columns, rows, [], pageSettings ?? new(595, 842, 48, 48, 48, 48), printArea, images,
            HeaderFooter: new(new("&A"), new("ページ &P / &N")));

    private static void ConfigureJapaneseFont()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-VariableFont_wght.ttf");
        Assert.True(File.Exists(fontPath), $"日本語フォントが見つかりません: {fontPath}");
        GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver("Noto Sans JP", fontPath);
    }

    private static byte[] CreateImageBytes(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(220, 120);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var red = new SKPaint { Color = SKColors.IndianRed };
        using var green = new SKPaint { Color = SKColors.SeaGreen };
        using var blue = new SKPaint { Color = SKColors.SteelBlue };
        canvas.DrawRect(10, 10, 60, 100, red);
        canvas.DrawCircle(110, 60, 45, green);
        canvas.DrawRoundRect(160, 20, 50, 80, 8, 8, blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private sealed record SampleOutput(
        ReportSheet Sheet,
        RenderDocument Layout,
        IReadOnlyList<DrawCommand> Commands,
        string PdfPath);
}

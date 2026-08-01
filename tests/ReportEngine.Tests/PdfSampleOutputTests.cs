using ClosedXML.Excel;
using PdfSharp.Fonts;
using ReportEngine.Drawing;
using ReportEngine.Layout;
using ReportEngine.Model;
using ReportEngine.PdfSharp;
using SkiaSharp;
using Xunit;

namespace ReportEngine.Tests;

public sealed class PdfSampleOutputTests
{
    private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "PdfSamples");

    [Fact]
    public void Generates_pdf_samples_for_visual_inspection()
    {
        ConfigureJapaneseFont();
        Directory.CreateDirectory(OutputDirectory);

        WriteSample("01-japanese.pdf", CreateJapaneseSheet());
        WriteSample("02-image.pdf", CreateImageSheet());
        WriteSample("03-wrapped-text.pdf", CreateWrappedTextSheet());
        WriteSample("04-text-decoration.pdf", CreateTextDecorationSheet());
    }

    private static void WriteSample(string fileName, ReportSheet sheet)
    {
        WriteExcelSample(Path.ChangeExtension(fileName, ".xlsx"), sheet);

        var document = new ReportLayoutEngine(new PdfSharpTextMeasurer()).Layout(sheet);
        var commands = new DrawCommandGeneratorPass().Generate(document);
        var path = Path.Combine(OutputDirectory, fileName);

        using (var output = File.Create(path))
            new PdfSharpRenderer().Render(commands, sheet.PageSettings, output);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0, $"PDF が出力されていません: {path}");
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
    }

    private static void WriteExcelSample(string fileName, ReportSheet sheet)
    {
        var path = Path.Combine(OutputDirectory, fileName);
        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet(sheet.Name);

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

        foreach (var image in sheet.Images ?? [])
        {
            using var stream = new MemoryStream(image.ImageBytes);
            worksheet.AddPicture(stream, $"image-{image.Anchor.Row}-{image.Anchor.Column}")!
                .MoveTo(worksheet.Cell(image.Anchor.Row, image.Anchor.Column),
                    (int)Math.Round(image.OffsetX * 96d / 72d), (int)Math.Round(image.OffsetY * 96d / 72d))
                .WithSize((int)Math.Round(image.Width * 96d / 72d), (int)Math.Round(image.Height * 96d / 72d));
        }

        workbook.SaveAs(path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 0, $"Excel が出力されていません: {path}");
        Assert.Equal("PK", System.Text.Encoding.ASCII.GetString(bytes, 0, 2));
    }

    private static void ApplyStyle(IXLCell cell, CellStyle style)
    {
        cell.Style.Font.FontName = style.Font.Family;
        cell.Style.Font.FontSize = style.Font.Size;
        cell.Style.Font.Bold = style.Font.Bold;
        cell.Style.Font.Italic = style.Font.Italic;
        cell.Style.Font.Underline = style.Font.Underline ? XLFontUnderlineValues.Single : XLFontUnderlineValues.None;
        cell.Style.Font.FontColor = ToColor(style.Font.Color);
        cell.Style.Fill.BackgroundColor = ToColor(style.Background);
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
            [new(1, 1)] = new("画像描画サンプル", style),
            [new(2, 1)] = new("赤、緑、青の図形を PNG として配置します。", CellStyle.Default)
        }, new Dictionary<int, ColumnDefinition> { [1] = new(440) },
            new Dictionary<int, RowDefinition> { [1] = new(30), [2] = new(28), [3] = new(150) },
            [new(new(3, 1), 0, 0, 220, 120, CreateImageBytes())]);
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

    private static ReportSheet CreateSheet(
        string name,
        IReadOnlyDictionary<CellAddress, ReportCell> cells,
        IReadOnlyDictionary<int, ColumnDefinition> columns,
        IReadOnlyDictionary<int, RowDefinition> rows,
        IReadOnlyList<ReportImage>? images = null) =>
        new(name, cells, columns, rows, [], new(595, 842, 48, 48, 48, 48), Images: images,
            HeaderFooter: new(new("&A"), new("ページ &P / &N")));

    private static void ConfigureJapaneseFont()
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "NotoSansJP-VariableFont_wght.ttf");
        Assert.True(File.Exists(fontPath), $"日本語フォントが見つかりません: {fontPath}");
        GlobalFontSettings.FontResolver ??= new PdfSharpFontResolver("Noto Sans JP", fontPath);
    }

    private static byte[] CreateImageBytes()
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
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
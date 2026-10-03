using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Model;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.Excel;

/// <summary>
/// ClosedXML による正規化前のワークブックレイアウト情報を読み取ります。
/// </summary>
internal static class WorkbookLayoutMetadataReader
{
    /// <summary>
    /// ワークシート名ごとの印刷倍率設定を読み取ります。
    /// </summary>
    /// <param name="input">読み取り対象のワークブックです。</param>
    /// <returns>ワークシート名をキーとする印刷倍率設定です。</returns>
    public static IReadOnlyDictionary<string, SheetPageSetupMetadata> ReadPageSetups(Stream input)
    {
        using var document = SpreadsheetDocument.Open(input, false);
        var workbookPart = document.WorkbookPart;
        if (workbookPart?.Workbook.Sheets is not { } sheets)
        {
            return new Dictionary<string, SheetPageSetupMetadata>();
        }

        var normalFont = ReadNormalFont(workbookPart);
        var result = new Dictionary<string, SheetPageSetupMetadata>(StringComparer.Ordinal);
        foreach (var sheet in sheets.Elements<S.Sheet>())
        {
            if (sheet.Name?.Value is not { } name || sheet.Id?.Value is not { } relationshipId ||
                workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                continue;
            }

            var worksheet = worksheetPart.Worksheet;
            var fitToPage = worksheet.SheetProperties?.PageSetupProperties?.FitToPage?.Value ?? false;
            var pageSetup = worksheet.GetFirstChild<S.PageSetup>();
            var sheetFormat = worksheet.SheetFormatProperties;
            var columns = worksheet.GetFirstChild<S.Columns>()?.Elements<S.Column>()
                .Where(column => column.Min?.Value is > 0 && column.Max?.Value is > 0)
                .Select(column => new RawColumnDefinition(
                    (int)Math.Min(column.Min!.Value, int.MaxValue),
                    (int)Math.Min(column.Max!.Value, int.MaxValue),
                    column.Width?.Value,
                    column.Hidden?.Value ?? false))
                .ToArray() ?? [];
            var rows = worksheet.GetFirstChild<S.SheetData>()?.Elements<S.Row>()
                .Where(row => row.RowIndex?.Value is > 0 &&
                    (row.CustomHeight?.Value == true || row.Hidden?.Value == true))
                .Select(row => new RawRowDefinition(
                    (int)Math.Min(row.RowIndex!.Value, int.MaxValue),
                    row.CustomHeight?.Value == true ? row.Height?.Value : null,
                    row.Hidden?.Value ?? false))
                .ToArray() ?? [];
            var pageOrder = pageSetup?.PageOrder?.Value == S.PageOrderValues.OverThenDown
                ? PrintPageOrder.OverThenDown
                : PrintPageOrder.DownThenOver;
            result[name] = new(
                fitToPage,
                pageSetup?.Scale?.Value,
                pageSetup?.FitToWidth?.Value,
                pageSetup?.FitToHeight?.Value,
                sheetFormat?.DefaultColumnWidth?.Value,
                sheetFormat?.DefaultRowHeight?.Value ?? 15,
                columns,
                rows,
                normalFont,
                ReadBreaks(worksheet.GetFirstChild<S.RowBreaks>()),
                ReadBreaks(worksheet.GetFirstChild<S.ColumnBreaks>()),
                pageOrder,
                sheetFormat?.BaseColumnWidth?.Value,
                worksheet.GetFirstChild<S.PrintOptions>()?.HorizontalCentered?.Value ?? false,
                worksheet.GetFirstChild<S.PrintOptions>()?.VerticalCentered?.Value ?? false);
        }

        return result;
    }

    private static IReadOnlyList<int> ReadBreaks(OpenXmlCompositeElement? breaks) => breaks?.Elements<S.Break>()
        .Where(pageBreak => pageBreak.Id?.Value is > 0)
        .Select(pageBreak => (int)Math.Min(pageBreak.Id!.Value, int.MaxValue))
        .Distinct()
        .OrderBy(value => value)
        .ToArray() ?? [];

    private static NormalFontMetadata? ReadNormalFont(WorkbookPart workbookPart)
    {
        var stylesheet = workbookPart.WorkbookStylesPart?.Stylesheet;
        var normalStyle = stylesheet?.CellStyles?.Elements<S.CellStyle>()
            .FirstOrDefault(style => style.BuiltinId?.Value == 0);
        var styleIndex = normalStyle?.FormatId?.Value ?? 0;
        var styleFormat = stylesheet?.CellStyleFormats?.Elements<S.CellFormat>().ElementAtOrDefault((int)styleIndex);
        var fontIndex = styleFormat?.FontId?.Value ?? 0;
        var font = stylesheet?.Fonts?.Elements<S.Font>().ElementAtOrDefault((int)fontIndex);
        if (font is null)
        {
            return null;
        }

        var family = font.FontName?.Val?.Value;
        if (string.IsNullOrWhiteSpace(family) && font.FontScheme?.Val?.Value is { } scheme)
        {
            var fontScheme = workbookPart.ThemePart?.Theme?.ThemeElements?.FontScheme;
            family = scheme == S.FontSchemeValues.Major
                ? fontScheme?.MajorFont?.LatinFont?.Typeface?.Value
                : fontScheme?.MinorFont?.LatinFont?.Typeface?.Value;
        }

        return string.IsNullOrWhiteSpace(family)
            ? null
            : new(family, font.FontSize?.Val?.Value ?? 11);
    }
}

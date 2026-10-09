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
    public static IReadOnlyDictionary<string, SheetPageSetupMetadata> ReadPageSetups(Stream input) =>
        Core.Excel.WorkbookLayoutMetadataReader.ReadPageSetups(input).ToDictionary(
            pair => pair.Key,
            pair => new SheetPageSetupMetadata(
                pair.Value.FitToPage,
                pair.Value.Scale,
                pair.Value.FitToWidth,
                pair.Value.FitToHeight,
                pair.Value.DefaultColumnWidth,
                pair.Value.DefaultRowHeight,
                pair.Value.Columns,
                pair.Value.Rows,
                pair.Value.NormalFont,
                pair.Value.RowBreaks,
                pair.Value.ColumnBreaks,
                CoreIntegration.CoreModelAdapter.ToPublic(pair.Value.PageOrder),
                pair.Value.BaseColumnWidth,
                pair.Value.HorizontalCentered,
                pair.Value.VerticalCentered),
            StringComparer.Ordinal);
}

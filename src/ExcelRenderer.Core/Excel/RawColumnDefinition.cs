namespace ExcelRenderer.Core.Excel;

/// <summary>
/// SpreadsheetML の列定義区間を表します。
/// </summary>
/// <param name="First">区間の先頭列です。</param>
/// <param name="Last">区間の末尾列です。</param>
/// <param name="Width">raw 列幅です。</param>
/// <param name="Hidden">列が非表示かどうかを示します。</param>
internal sealed record RawColumnDefinition(int First, int Last, double? Width, bool Hidden);

namespace ExcelRenderer.Excel;

/// <summary>
/// Normal セルスタイルが参照するフォントを表します。
/// </summary>
/// <param name="Family">解決済みのフォントファミリー名です。</param>
/// <param name="Size">ポイント単位のフォントサイズです。</param>
internal sealed record NormalFontMetadata(string Family, double Size);

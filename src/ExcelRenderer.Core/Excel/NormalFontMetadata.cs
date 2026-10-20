namespace ExcelRenderer.Core.Excel;

/// <summary>
/// Normal セルスタイルが参照するフォントを表します。
/// </summary>
/// <param name="Family">解決済みのフォントファミリー名です。</param>
/// <param name="Size">ポイント単位のフォントサイズです。</param>
/// <param name="Weight">Normal style weight.</param>
/// <param name="Italic">Whether Normal is italic.</param>
internal sealed record NormalFontMetadata(string Family, double Size, int Weight = 400, bool Italic = false);

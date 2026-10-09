namespace ExcelRenderer.Core.Abstractions;

/// <summary>
/// 文字列を描画するために必要な幅と高さを表します。
/// </summary>
internal readonly record struct TextSize(double Width, double Height);

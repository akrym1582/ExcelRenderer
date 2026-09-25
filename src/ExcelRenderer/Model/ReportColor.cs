namespace ExcelRenderer.Model;

/// <summary>
/// 赤、緑、青、およびアルファの各 8 ビット成分からなる色を表します。
/// </summary>
public readonly record struct ReportColor(byte Red, byte Green, byte Blue, byte Alpha = 255);

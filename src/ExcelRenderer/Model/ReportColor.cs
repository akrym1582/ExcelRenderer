namespace ExcelRenderer.Model;

/// <summary>
/// ReportColor が表すデータと操作を提供します.
/// </summary>
public readonly record struct ReportColor(byte Red, byte Green, byte Blue, byte Alpha = 255);

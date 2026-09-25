namespace ExcelRenderer.Fonts;

/// <summary>
/// ResolvedFont が表すデータと操作を提供します.
/// </summary>
public sealed record ResolvedFont(string Family, int Weight, bool Italic, string FilePath);

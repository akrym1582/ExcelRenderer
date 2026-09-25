namespace ExcelRenderer.Fonts;

/// <summary>
/// FontRequest が表すデータと操作を提供します.
/// </summary>
public sealed record FontRequest(string Family, int Weight = 400, bool Italic = false);

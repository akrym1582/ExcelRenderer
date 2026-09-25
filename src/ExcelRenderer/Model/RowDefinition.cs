namespace ExcelRenderer.Model;

/// <summary>
/// RowDefinition が表すデータと操作を提供します.
/// </summary>
public sealed record RowDefinition(double Height = 15, bool IsHidden = false);

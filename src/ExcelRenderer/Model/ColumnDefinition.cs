namespace ExcelRenderer.Model;

/// <summary>
/// ColumnDefinition が表すデータと操作を提供します.
/// </summary>
public sealed record ColumnDefinition(double Width = 64, bool IsHidden = false);

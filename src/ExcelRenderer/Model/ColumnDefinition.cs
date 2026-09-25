namespace ExcelRenderer.Model;

/// <summary>
/// シート内の列幅と非表示状態を表します。
/// </summary>
public sealed record ColumnDefinition(double Width = 64, bool IsHidden = false);

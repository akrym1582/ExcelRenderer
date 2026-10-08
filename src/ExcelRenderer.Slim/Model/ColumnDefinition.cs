namespace ExcelRenderer.Slim.Model;

/// <summary>
/// シート内の列幅と非表示状態を表します。
/// </summary>
internal sealed record ColumnDefinition(double Width = 64, bool IsHidden = false);

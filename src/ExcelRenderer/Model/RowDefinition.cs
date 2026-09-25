namespace ExcelRenderer.Model;

/// <summary>
/// シート内の行高と非表示状態を表します。
/// </summary>
public sealed record RowDefinition(double Height = 15, bool IsHidden = false);

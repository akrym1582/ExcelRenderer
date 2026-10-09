namespace ExcelRenderer.Core.Model;

/// <summary>
/// シート内の行高と非表示状態を表します。
/// </summary>
internal sealed record RowDefinition(double Height = 15, bool IsHidden = false);

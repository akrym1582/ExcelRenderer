namespace ExcelRenderer.Model;

/// <summary>
/// CellAddress が表すデータと操作を提供します.
/// </summary>
public readonly record struct CellAddress(int Row, int Column);

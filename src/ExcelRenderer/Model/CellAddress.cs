namespace ExcelRenderer.Model;

/// <summary>
/// 0 から始まる行番号と列番号によってシート上のセル位置を表します。
/// </summary>
public readonly record struct CellAddress(int Row, int Column);

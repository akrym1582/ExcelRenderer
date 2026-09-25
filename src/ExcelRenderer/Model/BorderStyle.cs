namespace ExcelRenderer.Model;

/// <summary>
/// セル矩形の左、上、右、および下の各辺に適用する罫線を表します。
/// </summary>
public sealed record BorderStyle(
    BorderSide? Left = null,
    BorderSide? Top = null,
    BorderSide? Right = null,
    BorderSide? Bottom = null);

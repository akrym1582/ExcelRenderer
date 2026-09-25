namespace ExcelRenderer.Model;

/// <summary>結合セルの外周を構成する元のセルアドレスと、その位置に残す罫線を表します。</summary>
public sealed record CellBorder(CellAddress Address, BorderStyle Border);

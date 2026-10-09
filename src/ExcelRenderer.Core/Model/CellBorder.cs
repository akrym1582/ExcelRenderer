namespace ExcelRenderer.Core.Model;

/// <summary>結合セルの外周を構成する元のセルアドレスと、その位置に残す罫線を表します。</summary>
internal sealed record CellBorder(CellAddress Address, BorderStyle Border);

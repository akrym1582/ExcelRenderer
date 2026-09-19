namespace ExcelRenderer.Model;

/// <summary>A perimeter fragment of a merged cell, at its original worksheet address.</summary>
public sealed record CellBorder(CellAddress Address, BorderStyle Border);

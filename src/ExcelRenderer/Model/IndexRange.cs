namespace ExcelRenderer.Model;

/// <summary>Represents an inclusive range of row or column indices.</summary>
public readonly record struct IndexRange(int First, int Last);

namespace ExcelRenderer.Model;

/// <summary>先頭と末尾の両方を含む行番号または列番号の範囲を表します。</summary>
public readonly record struct IndexRange(int First, int Last);

namespace ExcelRenderer.Model;

/// <summary>
/// CellRange が表すデータと操作を提供します.
/// </summary>
public readonly record struct CellRange(CellAddress First, CellAddress Last)
{
    /// <summary>
    /// Contains を実行します.
    /// </summary>
    /// <param name="address">address に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    public bool Contains(CellAddress address) =>
        address.Row >= First.Row && address.Row <= Last.Row &&
        address.Column >= First.Column && address.Column <= Last.Column;
}

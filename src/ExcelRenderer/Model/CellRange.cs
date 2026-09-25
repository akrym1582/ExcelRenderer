namespace ExcelRenderer.Model;

/// <summary>
/// 左上セルと右下セルを両端として含む矩形のセル範囲を表します。
/// </summary>
public readonly record struct CellRange(CellAddress First, CellAddress Last)
{
    /// <summary>
    /// 指定したセルアドレスが、この範囲の行および列の内側にあるかを判定します。
    /// </summary>
    /// <param name="address">範囲に含まれるかを判定するセルアドレスです。</param>
    /// <returns>指定したアドレスが範囲内にある場合は <see langword="true"/>、それ以外は <see langword="false"/> を返します。</returns>
    public bool Contains(CellAddress address) =>
        address.Row >= First.Row && address.Row <= Last.Row &&
        address.Column >= First.Column && address.Column <= Last.Column;
}

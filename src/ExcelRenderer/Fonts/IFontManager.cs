namespace ExcelRenderer.Fonts;

/// <summary>描画時のフォント要求を利用可能なフォントファイルへ解決します。</summary>
public interface IFontManager
{
    /// <summary>要求された属性に適合する利用可能なフォントを選択します。</summary>
    /// <param name="request">希望するフォントファミリー、ウェイト、および斜体の有無です。</param>
    /// <returns>選択されたフォントの属性と、そのフォントデータを格納するファイルパスを返します。</returns>
    ResolvedFont Resolve(FontRequest request);
}

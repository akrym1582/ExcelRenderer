namespace ExcelRenderer.Fonts;

/// <summary>描画時のフォント要求を利用可能なフォントファイルへ解決します。</summary>
public interface IFontManager
{
    /// <summary>要求された属性に適合する利用可能なフォントを選択します。</summary>
    /// <param name="request">希望するフォントファミリー、ウェイト、および斜体の有無です。</param>
    /// <returns>選択されたフォントの属性と、そのフォントデータを格納するファイルパスを返します。</returns>
    ResolvedFont Resolve(FontRequest request);

    /// <summary>指定テキストを表示できるフォントごとの連続範囲を解決します。</summary>
    /// <param name="text">解決するテキストです。</param>
    /// <param name="request">先頭候補の書式要求です。</param>
    /// <returns>同一フォントで連続して描画できるテキストランです。</returns>
    IReadOnlyList<TextRun> ResolveTextRuns(string text, FontRequest request);
}

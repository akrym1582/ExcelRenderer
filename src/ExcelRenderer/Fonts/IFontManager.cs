namespace ExcelRenderer.Fonts;

/// <summary>
/// IFontManager が表すデータと操作を提供します.
/// </summary>
public interface IFontManager
{
    /// <summary>
    /// Initializes a new instance of the class. 必要な設定を使って新しいインスタンスを初期化します.
    /// </summary>
    /// <param name="request">request に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    ResolvedFont Resolve(FontRequest request);
}

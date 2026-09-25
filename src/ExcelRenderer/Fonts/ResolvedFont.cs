namespace ExcelRenderer.Fonts;

/// <summary>フォント要求に対して選択された実際のフォントとファイルの場所を表します。</summary>
/// <param name="Family">選択されたフォントファミリー名です。</param>
/// <param name="Weight">選択されたフォントウェイトです。</param>
/// <param name="Italic">選択された書体が斜体の場合は <see langword="true"/> です。</param>
/// <param name="FilePath">フォントデータを格納するファイルのパスです。</param>
public sealed record ResolvedFont(string Family, int Weight, bool Italic, string FilePath);

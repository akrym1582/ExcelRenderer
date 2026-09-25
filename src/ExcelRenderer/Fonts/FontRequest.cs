namespace ExcelRenderer.Fonts;

/// <summary>解決を要求するフォントのファミリー名、ウェイト、および斜体の有無を表します。</summary>
/// <param name="Family">希望するフォントファミリー名です。</param>
/// <param name="Weight">希望するフォントウェイトです。400 は標準、700 は太字を表します。</param>
/// <param name="Italic">斜体を要求する場合は <see langword="true"/>、それ以外は <see langword="false"/> です。</param>
public sealed record FontRequest(string Family, int Weight = 400, bool Italic = false);

namespace ExcelRenderer.Rendering;

/// <summary>レンダリング要求が生成する出力形式を指定します。</summary>
public enum OutputFormat
{
    /// <summary>Portable Document Format（PDF）を生成します。</summary>
    Pdf,

    /// <summary>ページごとの Portable Network Graphics（PNG）画像を生成します。</summary>
    Png,

    /// <summary>ページごとの Scalable Vector Graphics（SVG）画像を生成します。</summary>
    Svg,

    /// <summary>ワークシートの内容を Markdown 文書として生成します。</summary>
    Markdown,
}

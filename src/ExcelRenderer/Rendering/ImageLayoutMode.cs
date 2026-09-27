namespace ExcelRenderer.Rendering;

/// <summary>PNG および SVG の画像配置方法を指定します。</summary>
public enum ImageLayoutMode
{
    /// <summary>印刷ページごとに画像を生成します。</summary>
    Paginated,

    /// <summary>各ワークシートを改ページなしの単一キャンバスとして生成します。</summary>
    Continuous,
}

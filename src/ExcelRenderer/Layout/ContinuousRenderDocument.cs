namespace ExcelRenderer.Layout;

/// <summary>改ページなしで配置された単一シートの描画結果を表します。</summary>
public sealed record ContinuousRenderDocument(RenderDocument Document, double Width, double Height);

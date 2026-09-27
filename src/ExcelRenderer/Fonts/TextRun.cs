namespace ExcelRenderer.Fonts;

/// <summary>一つのフォントフェイスで描画する、Unicode テキスト要素境界のテキスト範囲を表します。</summary>
public sealed record TextRun(string Text, ResolvedFont Font);

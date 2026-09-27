namespace ExcelRenderer.Rendering;

/// <summary>診断を報告した変換パイプラインの段階を指定します。</summary>
public enum DiagnosticStage
{
    /// <summary>入力ブックの読み取り段階です。</summary>
    Read,

    /// <summary>読み取った文書をページへ配置する段階です。</summary>
    Layout,

    /// <summary>配置済みのページを画像または文書へ描画する段階です。</summary>
    Render,

    /// <summary>生成物を出力先へ書き込む段階です。</summary>
    Write,
}

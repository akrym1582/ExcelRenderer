namespace ExcelRenderer.Fonts;

/// <summary>IVS の字形を選択するときに優先する同梱書体を指定します。</summary>
public enum IvsFontStyle
{
    /// <summary>Noto Sans CJK JP を優先し、必要な場合だけ IPAmj 明朝へフォールバックします。</summary>
    Gothic,

    /// <summary>Noto Serif CJK JP を優先し、必要な場合だけ IPAmj 明朝へフォールバックします。</summary>
    Mincho,
}

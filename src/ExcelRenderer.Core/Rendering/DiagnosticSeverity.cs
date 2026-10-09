namespace ExcelRenderer.Core.Rendering;

/// <summary>変換診断の重大度を指定します。</summary>
internal enum DiagnosticSeverity
{
    /// <summary>処理を続行でき、利用者への参考情報となる診断です。</summary>
    Info,

    /// <summary>処理を続行できるものの、出力結果に注意が必要な診断です。</summary>
    Warning,

    /// <summary>変換結果の信頼性に影響する診断です。</summary>
    Error,
}

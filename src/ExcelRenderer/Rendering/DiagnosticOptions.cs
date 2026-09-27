namespace ExcelRenderer.Rendering;

/// <summary>変換診断の収集方法と失敗判定を制御します。</summary>
public sealed record DiagnosticOptions
{
    /// <summary>Gets a value indicating whether non-information diagnostics are failures. 情報以外の診断を失敗として扱うかどうかを示します。</summary>
    public bool StrictMode { get; init; }

    /// <summary>Gets diagnostic codes treated as errors regardless of severity. 重大度にかかわらずエラーとして扱う診断コードの一覧です。</summary>
    public IReadOnlyCollection<string> TreatAsErrors { get; init; } = Array.Empty<string>();

    /// <summary>Gets diagnostic codes omitted from results. 結果へ追加しない診断コードの一覧です。</summary>
    public IReadOnlyCollection<string> SuppressedCodes { get; init; } = Array.Empty<string>();

    /// <summary>Gets the maximum number of retained diagnostics. 保持する診断の最大件数です。超過分は集約して通知されます。</summary>
    public int MaxDiagnostics { get; init; } = 100;
}

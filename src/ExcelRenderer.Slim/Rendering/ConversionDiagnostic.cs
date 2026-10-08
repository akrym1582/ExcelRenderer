namespace ExcelRenderer.Slim.Rendering;

/// <summary>重複を集約した変換診断を保持します。</summary>
/// <param name="Code">診断を識別するコードです。</param>
/// <param name="Severity">診断の重大度です。</param>
/// <param name="Stage">診断を報告した変換段階です。</param>
/// <param name="Message">診断の説明文です。</param>
/// <param name="SheetName">診断に関係するワークシート名です。</param>
/// <param name="CellRange">診断に関係するセル範囲です。</param>
/// <param name="ObjectId">診断に関係する図形などの ID です。</param>
/// <param name="SourcePageNumber">診断に関係する元ページ番号です。</param>
/// <param name="OccurrenceCount">同じ診断が発生した回数です。</param>
internal sealed record ConversionDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    DiagnosticStage Stage,
    string Message,
    string? SheetName = null,
    string? CellRange = null,
    string? ObjectId = null,
    int? SourcePageNumber = null,
    int OccurrenceCount = 1);

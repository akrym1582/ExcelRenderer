using System.Collections.ObjectModel;
using ExcelRenderer.Fonts;

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

/// <summary>変換診断の重大度を指定します。</summary>
public enum DiagnosticSeverity
{
    /// <summary>処理を続行でき、利用者への参考情報となる診断です。</summary>
    Info,

    /// <summary>処理を続行できるものの、出力結果に注意が必要な診断です。</summary>
    Warning,

    /// <summary>変換結果の信頼性に影響する診断です。</summary>
    Error,
}

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

/// <summary><see cref="ExcelConverter.RenderAsync"/> が生成した生成物を受け取ります。</summary>
/// <remarks>
/// <see cref="OpenAsync"/> が返すストリームの所有権はシンクに残り、レンダラーは破棄しません。
/// 呼び出しは直列化されます。正常なオープンの後には、<see cref="CompleteAsync"/> または
/// <see cref="AbortAsync"/> のいずれかがちょうど一度呼び出されます。
/// </remarks>
public interface IRenderOutputSink
{
    /// <summary>生成物の内容を書き込むストリームを開きます。</summary>
    /// <param name="artifact">これから書き込む生成物の識別情報です。</param>
    /// <param name="cancellationToken">オープン処理のキャンセルを通知するトークンです。</param>
    /// <returns>生成物を書き込むストリームを返します。ストリームの所有権はシンクが保持します。</returns>
    /// <exception cref="OperationCanceledException">オープン処理がキャンセルされた場合にスローされます。</exception>
    ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken);

    /// <summary>生成物の書き込みが完了したことをシンクへ通知します。</summary>
    /// <param name="artifact">書き込みが完了した生成物の識別情報です。</param>
    /// <param name="byteLength">生成物へ書き込まれたバイト数です。</param>
    /// <param name="cancellationToken">完了処理のキャンセルを通知するトークンです。</param>
    /// <returns>完了処理が終了したときに完了する値タスクを返します。</returns>
    /// <exception cref="OperationCanceledException">完了処理がキャンセルされた場合にスローされます。</exception>
    ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken);

    /// <summary>生成途中の生成物を破棄し、失敗をシンクへ通知します。</summary>
    /// <param name="artifact">破棄する生成物の識別情報です。</param>
    /// <param name="error">生成を中断した例外です。</param>
    /// <param name="cancellationToken">中断処理のキャンセルを通知するトークンです。</param>
    /// <returns>中断処理が終了したときに完了する値タスクを返します。</returns>
    /// <exception cref="OperationCanceledException">中断処理がキャンセルされた場合にスローされます。</exception>
    ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken);
}

/// <summary>変換元ブックを準備するときに使用する上限と方針です。</summary>
public sealed record WorkbookInputOptions
{
    /// <summary>Gets the maximum number of bytes retained in memory. 入力をメモリ上に保持できる最大バイト数です。</summary>
    public long MemoryThresholdBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets the maximum number of accepted input bytes. 受け入れる入力ストリームの最大バイト数です。</summary>
    public long MaxInputBytes { get; init; } = 128 * 1024 * 1024;

    /// <summary>Gets a value indicating whether temporary files may be used after the memory limit is exceeded. メモリ上限を超えた入力について、一時ファイルの利用を許可するかどうかを示します。</summary>
    public bool AllowTemporaryFiles { get; init; }

    /// <summary>Gets the directory for temporary files. 一時ファイルを作成するディレクトリです。<see langword="null"/> の場合は既定の場所を使用します。</summary>
    public string? TemporaryDirectory { get; init; }

    /// <summary>Gets the maximum number of entries allowed in the input ZIP. 入力 ZIP に含められるエントリ数の最大値です。</summary>
    public int MaxZipEntryCount { get; init; } = 10_000;

    /// <summary>Gets the maximum total size of expanded ZIP entries. 展開後の ZIP エントリ合計サイズの最大バイト数です。</summary>
    public long MaxUncompressedZipBytes { get; init; } = 512 * 1024 * 1024;
}

/// <summary>変換対象とするワークシートとページを選択します。</summary>
public sealed record SelectionOptions
{
    /// <summary>Gets the worksheet names to select. 名前で選択するワークシートの一覧です。<see langword="null"/> の場合はすべてを選択します。</summary>
    public IReadOnlyList<string>? SheetNames { get; init; }

    /// <summary>Gets the document page numbers to select. 文書ページ番号で選択するページの一覧です。<see langword="null"/> の場合はすべてを選択します。</summary>
    public IReadOnlyList<int>? Pages { get; init; }
}

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

/// <summary>ストリームを入力として実行する変換要求を記述します。</summary>
public sealed record RenderRequest
{
    /// <summary>Gets the output format to generate. 生成する出力形式です。</summary>
    public OutputFormat OutputFormat { get; init; }

    /// <summary>Gets the worksheet and page selection criteria. ワークシートとページの選択条件です。</summary>
    public SelectionOptions Selection { get; init; } = new();

    /// <summary>Gets the diagnostic collection and failure criteria. 診断の収集と失敗判定の条件です。</summary>
    public DiagnosticOptions DiagnosticOptions { get; init; } = new();

    /// <summary>Gets the input-size and ZIP-expansion criteria. 入力ストリームのサイズと ZIP 展開に関する条件です。</summary>
    public WorkbookInputOptions Input { get; init; } = new();

    /// <summary>Gets the font selection criteria for PDF, PNG, and SVG rendering. PDF、PNG、および SVG の描画に使用するフォントの選択条件です。</summary>
    public FontOptions FontOptions { get; init; } = new();

    /// <summary>Gets the PNG output resolution in DPI. PNG 出力の解像度を DPI で指定します。</summary>
    public double Dpi { get; init; } = 96;
}

/// <summary>内容を開く前の生成物を識別します。</summary>
/// <param name="ArtifactId">変換結果内で生成物を一意に識別する ID です。</param>
/// <param name="Kind">生成物の論理的な種類です。</param>
/// <param name="MediaType">生成物の MIME タイプです。</param>
/// <param name="RelativeName">出力先のルートからの相対パスです。</param>
/// <param name="SourcePageNumber">対応する元文書ページ番号です。ページ単位でない生成物では <see langword="null"/> です。</param>
/// <param name="OutputPageNumber">選択後の出力ページ番号です。ページ単位でない生成物では <see langword="null"/> です。</param>
public sealed record ArtifactDescriptor(string ArtifactId, string Kind, string MediaType, string RelativeName, int? SourcePageNumber = null, int? OutputPageNumber = null);

/// <summary>完了した変換生成物を記述します。</summary>
/// <param name="Descriptor">生成物の識別情報です。</param>
/// <param name="ByteLength">生成物へ書き込まれたバイト数です。</param>
public sealed record ArtifactMetadata(ArtifactDescriptor Descriptor, long ByteLength);

/// <summary>描画ページの元文書上および出力上の識別情報を記述します。</summary>
/// <param name="SourceSheetIndex">元ブック内のワークシート番号です。</param>
/// <param name="SourceSheetName">元ワークシートの名前です。</param>
/// <param name="SourcePageNumber">元ワークシート内のページ番号です。</param>
/// <param name="DocumentPageNumber">シートをまたいだ文書全体のページ番号です。</param>
/// <param name="OutputPageNumber">選択後に割り当てられた出力ページ番号です。出力対象外の場合は <see langword="null"/> です。</param>
/// <param name="WidthPoints">ページ幅を PDF ポイントで表した値です。</param>
/// <param name="HeightPoints">ページ高さを PDF ポイントで表した値です。</param>
/// <param name="PixelWidth">PNG 出力時のページ幅をピクセルで表した値です。</param>
/// <param name="PixelHeight">PNG 出力時のページ高さをピクセルで表した値です。</param>
/// <param name="Dpi">ピクセル寸法の算出に使用した解像度です。</param>
public sealed record RenderPageDescriptor(
    int SourceSheetIndex,
    string SourceSheetName,
    int SourcePageNumber,
    int DocumentPageNumber,
    int? OutputPageNumber,
    double WidthPoints,
    double HeightPoints,
    int? PixelWidth = null,
    int? PixelHeight = null,
    double? Dpi = null);

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
public sealed record ConversionDiagnostic(
    string Code,
    DiagnosticSeverity Severity,
    DiagnosticStage Stage,
    string Message,
    string? SheetName = null,
    string? CellRange = null,
    string? ObjectId = null,
    int? SourcePageNumber = null,
    int OccurrenceCount = 1);

/// <summary>変換で利用者から観測できる結果を保持します。</summary>
/// <param name="SchemaVersion">この結果を解釈するマニフェストのスキーマバージョンです。</param>
/// <param name="CompletionStatus">変換の完了状態を表す文字列です。</param>
/// <param name="SelectedSheets">変換対象として選択されたワークシート名の一覧です。</param>
/// <param name="Pages">描画されたページの一覧です。</param>
/// <param name="Artifacts">出力された生成物の一覧です。</param>
/// <param name="Diagnostics">変換中に収集された診断の一覧です。</param>
public sealed record ConversionResult(
    int SchemaVersion,
    string CompletionStatus,
    IReadOnlyList<string> SelectedSheets,
    IReadOnlyList<RenderPageDescriptor> Pages,
    IReadOnlyList<ArtifactMetadata> Artifacts,
    IReadOnlyList<ConversionDiagnostic> Diagnostics);

/// <summary>診断または生成物が作成された後に変換を完了できない場合にスローされる例外です。</summary>
public sealed class ConversionException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ConversionException"/> class. 指定した失敗情報を持つ例外を初期化します。</summary>
    /// <param name="message">失敗の説明文です。</param>
    /// <param name="innerException">失敗の原因となった例外です。原因がない場合は <see langword="null"/> です。</param>
    /// <param name="diagnostics">変換中に収集された診断の一覧です。</param>
    /// <param name="artifacts">失敗前に完了した生成物の一覧です。</param>
    public ConversionException(string message, Exception? innerException, IReadOnlyList<ConversionDiagnostic> diagnostics, IReadOnlyList<ArtifactMetadata> artifacts)
        : base(message, innerException)
    {
        CollectedDiagnostics = diagnostics;
        CompletedArtifacts = artifacts;
    }

    /// <summary>Gets the diagnostics collected during conversion. 変換中に収集された診断の一覧を取得します。</summary>
    public IReadOnlyList<ConversionDiagnostic> CollectedDiagnostics { get; }

    /// <summary>Gets the artifacts completed before conversion failed. 変換失敗前に完了した生成物の一覧を取得します。</summary>
    public IReadOnlyList<ArtifactMetadata> CompletedArtifacts { get; }
}

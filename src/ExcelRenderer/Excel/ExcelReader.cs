using ExcelRenderer.Fonts;
using ExcelRenderer.Model;
using ExcelRenderer.Rendering;

namespace ExcelRenderer.Excel;

/// <summary>Excel ブックのワークシート、セル、印刷設定、画像、および図形をレンダリング用モデルとして読み込みます。</summary>
public sealed class ExcelReader
{
    private readonly Dictionary<NormalFontMetadata, double> maximumDigitWidths = new();
    private readonly IFontManager? fontManager;

    /// <summary>Initializes a new instance of the <see cref="ExcelReader"/> class.</summary>
    public ExcelReader()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ExcelReader"/> class.</summary>
    /// <param name="fontManager">Normal スタイルの列幅計測に使用するフォントマネージャーです。</param>
    public ExcelReader(IFontManager fontManager)
    {
        this.fontManager = fontManager ?? throw new ArgumentNullException(nameof(fontManager));
    }

    /// <summary>指定した Excel ファイルを読み取り、各ワークシートの内容をレンダリング用ドキュメントへ変換します。</summary>
    /// <param name="path">読み取る Excel ファイルのパスです。</param>
    /// <returns>ブック内のワークシートを元の順序で格納したレンダリング用ドキュメントを返します。</returns>
    public ReportDocument Read(string path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        using var input = File.OpenRead(path);
        return Read(input);
    }

    /// <summary>ストリームの現在位置からブックを読み取り、呼び出し元が所有するストリームを閉じずに処理します。</summary>
    /// <param name="input">読み取り対象の Excel データを含むストリームです。</param>
    /// <returns>ブック内のワークシートを元の順序で格納したレンダリング用ドキュメントを返します。</returns>
    public ReportDocument Read(Stream input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        using var prepared = WorkbookInputPreparer.ReadAsync(
            input,
            new WorkbookInputOptions
            {
                MaxInputBytes = long.MaxValue,
                MemoryThresholdBytes = long.MaxValue,
                MaxZipEntryCount = int.MaxValue,
                MaxUncompressedZipBytes = long.MaxValue,
            },
            CancellationToken.None).GetAwaiter().GetResult();
        return Read(prepared, null);
    }

    /// <summary>読み取り時の診断情報を収集しながら、バイト配列の Excel ブックを読み取ります。</summary>
    /// <param name="workbookBytes">Excel ブック全体を格納したバイト配列です。</param>
    /// <param name="diagnostics">読み取り中に発生した診断情報を追加するコレクターです。</param>
    /// <returns>ブック内のワークシートを元の順序で格納したレンダリング用ドキュメントを返します。</returns>
    internal ReportDocument Read(byte[] workbookBytes, DiagnosticCollector? diagnostics)
    {
        using var prepared = new SpillableBufferStream(new RenderBufferOptions());
        prepared.Write(workbookBytes, 0, workbookBytes.Length);
        using var owner = new PreparedWorkbook(prepared);
        return Read(owner, diagnostics);
    }

    /// <summary>Reads workbook models with optional selected-sheet body projection.</summary>
    /// <param name="source">The source used by this operation.</param>
    /// <param name="diagnostics">The diagnostics used by this operation.</param>
    /// <param name="selectedSheets">The selectedSheets used by this operation.</param>
    /// <returns>The planned or generated result.</returns>
    internal ReportDocument Read(PreparedWorkbook source, DiagnosticCollector? diagnostics, IReadOnlyList<string>? selectedSheets = null)
    {
        var document = ReadCore(source, diagnostics, selectedSheets);
        var styles = new StylePool();
        return ConversionMetrics.Measure("model", () => new ReportDocument(document.Sheets.Select(sheet =>
        {
            var view = CoreIntegration.CoreModelAdapter.ToPublic(sheet, styles);
            return view with
            {
                Cells = view.Cells.ToDictionary(pair => pair.Key, pair => pair.Value),
                Columns = view.Columns.ToDictionary(pair => pair.Key, pair => pair.Value),
                Rows = view.Rows.ToDictionary(pair => pair.Key, pair => pair.Value),
            };
        }).ToArray()));
    }

    /// <summary>Reads the neutral model directly for conversion sessions.</summary>
    /// <param name="source">The prepared input.</param>
    /// <param name="diagnostics">The product collector.</param>
    /// <param name="selectedSheets">The optional selected bodies.</param>
    /// <returns>The neutral workbook with full typed metadata.</returns>
    internal Core.Model.ReportDocument ReadCore(PreparedWorkbook source, DiagnosticCollector? diagnostics, IReadOnlyList<string>? selectedSheets = null)
    {
        var coreDiagnostics = diagnostics is null ? null : new Core.Rendering.DiagnosticCollector(diagnostic => diagnostics.Add(CoreIntegration.CoreDiagnosticAdapter.ToPublic(diagnostic)));
        return new CoreIntegration.FullExcelReader(fontManager, diagnostics, maximumDigitWidths)
            .Read(source.CoreWorkbook, coreDiagnostics, selectedSheets);
    }
}

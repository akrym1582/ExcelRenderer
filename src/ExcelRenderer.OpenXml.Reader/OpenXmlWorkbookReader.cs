using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ExcelNumberFormat;
using ExcelRenderer.OpenXml.Reader.Diagnostics;
using ExcelRenderer.OpenXml.Reader.Formatting;
using ExcelRenderer.OpenXml.Reader.Infrastructure;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.Model.Styles;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml.Reader;

/// <summary>Read-only streaming XLSX reader for rendering data. Does not calculate formulas.</summary>
public sealed partial class OpenXmlWorkbookReader : IWorkbookDataReader
{
    private readonly SpreadsheetDocument document;
    private readonly OpenXmlReaderOptions options;
    private readonly StyleTable styles;
    private readonly IReadOnlyList<SheetInfo> sheets;
    private readonly WorkbookInfo workbook;
    private readonly IReadOnlyList<int> selectedWorksheetIndices;
    private readonly Dictionary<int, WorksheetPart> parts = new Dictionary<int, WorksheetPart>();
    private readonly HashSet<int> selected = new HashSet<int>();
    private readonly Dictionary<int, SheetMetadata> metadata = new Dictionary<int, SheetMetadata>();
    private readonly Dictionary<string, NumberFormat> numberFormats = new Dictionary<string, NumberFormat>(StringComparer.Ordinal);
    private List<StringEntry>? strings;
    private OpenXmlReader? activeReader;
    private int busy;
    private bool disposed;
    private TimeSpan sharedStringLoadTime;

    private OpenXmlWorkbookReader(SpreadsheetDocument document, OpenXmlReaderOptions options, CancellationToken token)
    {
        this.document = document;
        this.options = options;
        var part = document.WorkbookPart ?? throw new InvalidDataException("Workbook part is missing.");
        var names = new List<DefinedNameInfo>();
        var list = new List<SheetInfo>();
        var date1904 = false;
        using (var reader = XmlValues.CreateReader(part))
        {
            while (XmlValues.Read(reader))
            {
                token.ThrowIfCancellationRequested();
                if (!reader.IsStartElement)
                {
                    continue;
                }

                if (reader.ElementType == typeof(S.WorkbookProperties))
                {
                    date1904 = XmlValues.Bool(XmlValues.Load(reader), "date1904") ?? false;
                }
                else if (reader.ElementType == typeof(S.Sheet))
                {
                    var sheet = (S.Sheet)reader.LoadCurrentElement()!;
                    var target = part.GetPartById(sheet.Id?.Value ?? throw new InvalidDataException("Sheet relationship is missing."));
                    var index = list.Count + 1;
                    var kind = target is WorksheetPart ? "Worksheet" : target is ChartsheetPart ? "Chartsheet" : "Other";
                    list.Add(new SheetInfo(index, sheet.SheetId?.Value ?? throw new InvalidDataException("Sheet ID is missing."), sheet.Name?.Value ?? throw new InvalidDataException("Sheet name is missing."), kind, sheet.State?.InnerText ?? "visible"));
                    if (target is WorksheetPart worksheet)
                    {
                        parts.Add(index, worksheet);
                    }
                }
                else if (reader.ElementType == typeof(S.DefinedName))
                {
                    var e = XmlValues.Load(reader);
                    names.Add(new DefinedNameInfo(XmlValues.Attr(e, "name") ?? string.Empty, e.Value, XmlValues.Int(e, "localSheetId")));
                }
            }
        }

        sheets = list.AsReadOnly();
        if (options.SelectedSheetNames != null)
        {
            foreach (var name in options.SelectedSheetNames)
            {
                var sheet = list.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal)) ?? throw new ArgumentException("Worksheet was not found: " + name, nameof(options));
                selected.Add(sheet.Index);
            }
        }
        else
        {
            selected.UnionWith(parts.Keys);
        }

        selectedWorksheetIndices = list.Where(s => parts.ContainsKey(s.Index) && selected.Contains(s.Index)).Select(s => s.Index).ToList().AsReadOnly();
        styles = new StyleTable(part, options, token);
        workbook = new WorkbookInfo(date1904, names.AsReadOnly(), styles.DefaultFont, styles.Theme);
    }

    /// <summary>Gets the Workbook metadata.</summary>
    public WorkbookInfo Workbook
    {
        get
        {
            EnsureAlive();
            return workbook;
        }
    }

    /// <summary>Gets the Sheets metadata.</summary>
    public IReadOnlyList<SheetInfo> Sheets
    {
        get
        {
            EnsureAlive();
            return sheets;
        }
    }

    /// <summary>Gets selected worksheet positions in original workbook order, excluding chart sheets.</summary>
    public IReadOnlyList<int> SelectedWorksheetIndices
    {
        get
        {
            EnsureAlive();
            return selectedWorksheetIndices;
        }
    }

    /// <summary>Gets the shared-string entries currently retained; zero before first use.</summary>
    public int LoadedSharedStringCount
    {
        get
        {
            EnsureAlive();
            return strings?.Count ?? 0;
        }
    }

    /// <summary>Gets the saved cell-XF count, excluding the implicit default when styles are absent.</summary>
    public int StyleRecordCount
    {
        get
        {
            EnsureAlive();
            return styles.Count;
        }
    }

    /// <summary>Gets time spent loading shared strings, also included in enumeration time.</summary>
    public TimeSpan SharedStringLoadTime
    {
        get
        {
            EnsureAlive();
            return sharedStringLoadTime;
        }
    }

    /// <summary>Opens an already validated, readable, seekable XLSX stream at position zero.</summary>
    /// <returns>The disposable reader.</returns>
    /// <param name="input">The input value.</param>
    /// <param name="options">The options value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    public static OpenXmlWorkbookReader Open(Stream input, OpenXmlReaderOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        if (!input.CanRead || !input.CanSeek)
        {
            throw new NotSupportedException("Input must be readable and seekable; prepare it before calling this low-level API.");
        }

        if (input.Position != 0)
        {
            throw new ArgumentException("Input position must be zero.", nameof(input));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = (options ?? new OpenXmlReaderOptions()).Snapshot();
        var wrapper = new LeaveOpenStream(input);
        SpreadsheetDocument? doc = null;
        try
        {
            doc = SpreadsheetDocument.Open(wrapper, false, new OpenSettings { MaxCharactersInPart = snapshot.MaxCharactersInPart });
            return new OpenXmlWorkbookReader(doc, snapshot, cancellationToken);
        }
        catch (System.Xml.XmlException e)
        {
            doc?.Dispose();
            wrapper.Dispose();
            throw new InvalidDataException("Invalid XML or MaxCharactersInPart exceeded.", e);
        }
        catch
        {
            doc?.Dispose();
            wrapper.Dispose();
            throw;
        }
    }

    /// <summary>Scans and caches sparse metadata; selected body access is not required.</summary>
    /// <returns>Worksheet metadata in source units.</returns>
    /// <param name="sheetIndex">The sheetIndex value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    public WorksheetInfo ReadWorksheetInfo(int sheetIndex, CancellationToken cancellationToken = default)
    {
        Enter();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetMetadata(sheetIndex, cancellationToken).Info;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>Enumerates only XML-present cells. At most one enumeration may be active.</summary>
    /// <returns>Immutable cell snapshots, reread on each enumeration.</returns>
    /// <param name="sheetIndex">The sheetIndex value.</param>
    /// <param name="cancellationToken">The cancellationToken value.</param>
    public IEnumerable<CellRecord> EnumerateCells(int sheetIndex, CancellationToken cancellationToken = default)
    {
        EnsureAlive();
        GetPart(sheetIndex);
        if (!selected.Contains(sheetIndex))
        {
            throw new InvalidOperationException("Worksheet body was not selected.");
        }

        return EnumerateCore(sheetIndex, cancellationToken);
    }

    /// <summary>Resolves a workbook-local style ID, including row/column styles without a cell snapshot.</summary>
    /// <param name="styleIndex">Zero-based effective cell-XF index.</param>
    /// <returns>Shared immutable source style metadata.</returns>
    public CellStyleInfo ResolveStyle(int styleIndex)
    {
        EnsureAlive();
        return styles.Resolve(styleIndex);
    }

    /// <summary>Resolves a cell's effective style while this reader is alive.</summary>
    /// <returns>Shared immutable style metadata.</returns>
    /// <param name="cell">The cell value.</param>
    public CellStyleInfo ResolveStyle(CellRecord cell)
    {
        EnsureAlive();
        return ResolveStyle(cell.StyleIndex);
    }

    /// <summary>Formats stored values without evaluating formulas or simulating column width.</summary>
    /// <returns>Display text and this call's diagnostics.</returns>
    /// <param name="cell">The cell value.</param>
    public CellDisplayResult FormatCell(CellRecord cell)
    {
        EnsureAlive();
        List<ReaderDiagnostic>? diagnostics = null;
        if (cell.Formula != null && !cell.Formula.HasCachedResult)
        {
            Report(cell, ref diagnostics, ReaderDiagnosticCodes.FormulaCachedValueMissing, "Formula has no stored result; calculation is not supported.");
            return new CellDisplayResult(string.Empty, diagnostics == null ? Array.Empty<ReaderDiagnostic>() : diagnostics.AsReadOnly());
        }

        var value = cell.Value;
        var text = value.Text ?? string.Empty;
        if (value.Type == CellValueType.Boolean)
        {
            text = value.Boolean == true ? "TRUE" : "FALSE";
        }
        else if (value.Type == CellValueType.Error && cell.Formula != null)
        {
            Report(cell, ref diagnostics, ReaderDiagnosticCodes.FormulaResultError, "Stored formula result is an Excel error.");
        }
        else if (value.Type == CellValueType.Number || value.Type == CellValueType.String)
        {
            var style = ResolveStyle(cell);
            var code = style.NumberFormatCode;
            if (code == null || (code != "General" && (code.IndexOf("[$", StringComparison.Ordinal) >= 0 || code.IndexOf("DBNum", StringComparison.OrdinalIgnoreCase) >= 0 || code.IndexOf("gg", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                Report(cell, ref diagnostics, ReaderDiagnosticCodes.UnsupportedNumberFormat, "Number format is unsupported; General display is used.");
                code = "General";
            }

            if (!numberFormats.TryGetValue(code, out var format))
            {
                format = new NumberFormat(code);
                if (numberFormats.Count < 8192)
                {
                    numberFormats.Add(code, format);
                }
            }

            if (!format.IsValid)
            {
                Report(cell, ref diagnostics, ReaderDiagnosticCodes.UnsupportedNumberFormat, "Number format is invalid; General display is used.");
                format = new NumberFormat("General");
            }

            text = format.Format(value.Type == CellValueType.Number ? (object)value.Number!.Value : text, options.Culture!, workbook.Date1904);
        }

        return new CellDisplayResult(text, diagnostics == null ? Array.Empty<ReaderDiagnostic>() : diagnostics.AsReadOnly());
    }

    /// <summary>Closes package resources while leaving the caller's input stream open.</summary>
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            activeReader?.Dispose();
            activeReader = null;
            document.Dispose();
            strings = null;
            metadata.Clear();
            parts.Clear();
            imageParts.Clear();
            images.Clear();
            numberFormats.Clear();
            styles.Release();
            options.DiagnosticSink = null;
        }
    }

    /// <summary>Enumerates dependency cells independently of rendering selection.</summary>
    /// <param name="sheetIndex">Original worksheet position.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Immutable source snapshots.</returns>
    internal IEnumerable<CellRecord> EnumerateCalculationCells(int sheetIndex, CancellationToken cancellationToken)
    {
        EnsureAlive();
        GetPart(sheetIndex);
        return EnumerateCore(sheetIndex, cancellationToken);
    }

    private void Report(CellRecord cell, ref List<ReaderDiagnostic>? diagnostics, string code, string message)
    {
        var diagnostic = new ReaderDiagnostic(code, "Warning", cell.SheetIndex, cell.Address, message);
        diagnostics ??= new List<ReaderDiagnostic>();
        diagnostics.Add(diagnostic);
        options.DiagnosticSink?.Invoke(diagnostic);
    }

    private WorksheetPart GetPart(int index)
    {
        EnsureAlive();
        if (index < 1 || index > sheets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return parts.TryGetValue(index, out var part) ? part : throw new NotSupportedException("Sheet kind does not contain worksheet cells.");
    }

    private void EnsureAlive()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(OpenXmlWorkbookReader));
        }
    }

    private void Enter()
    {
        EnsureAlive();
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("Another worksheet operation is active on this reader.");
        }
    }

    private void Exit() => Interlocked.Exchange(ref busy, 0);
}

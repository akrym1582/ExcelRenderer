using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using ExcelRenderer.Formulas.Data;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;

namespace ExcelRenderer.OpenXml.Calculation;

/// <summary>Evaluates reached cells against one immutable workbook snapshot.</summary>
public sealed class WorkbookCalculationSession
{
    private readonly Snapshot source;
    private readonly FormulaSession session;

    private WorkbookCalculationSession(Snapshot source, FormulaEvaluationOptions? options, CancellationToken token)
    {
        this.source = source;
        options ??= new FormulaEvaluationOptions();
        var effective = new FormulaEvaluationOptions
        {
            DateSystem = source.DateSystem,
            MaxFormulaLength = options.MaxFormulaLength,
            MaxAstNodes = options.MaxAstNodes,
            MaxSyntaxDepth = options.MaxSyntaxDepth,
            MaxDependencyDepth = options.MaxDependencyDepth,
            MaxFormulaCells = options.MaxFormulaCells,
            MaxCellVisits = options.MaxCellVisits,
            MaxEvaluationSteps = options.MaxEvaluationSteps,
            MaxStringLength = options.MaxStringLength,
            MaxCachedStringCharacters = options.MaxCachedStringCharacters,
        };
        session = new FormulaEngine().CreateSession(source, effective, token);
    }

    /// <summary>Gets successful or failed cells reached by this session.</summary>
    internal IReadOnlyDictionary<FormulaCellAddress, FormulaEvaluationResult> EvaluatedCells => session.EvaluatedCells;

    /// <summary>Gets all source formula cell addresses.</summary>
    internal IEnumerable<FormulaCellAddress> FormulaCells => source.Cells.Where(pair => pair.Value.Formula != null).Select(pair => pair.Key);

    /// <summary>Creates a session before any source mutation.</summary>
    /// <param name="reader">Reader selecting all dependency worksheets.</param>
    /// <param name="options">Calculation limits.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>A session whose cached results are isolated from later changes.</returns>
    public static WorkbookCalculationSession Create(IWorkbookDataReader reader, FormulaEvaluationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        return new WorkbookCalculationSession(new Snapshot(reader, cancellationToken), options, cancellationToken);
    }

    /// <summary>Creates a condition-evaluation session whose references use saved typed values, without recalculating source formulas.</summary>
    /// <param name="reader">Workbook reader including all dependency sheets.</param>
    /// <param name="options">Calculation limits.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>One shared saved-value snapshot and evaluation cache.</returns>
    public static WorkbookCalculationSession CreateSavedValueSession(IWorkbookDataReader reader, FormulaEvaluationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        return new WorkbookCalculationSession(new Snapshot(reader, cancellationToken, false), options, cancellationToken);
    }

    /// <summary>Evaluates arbitrary parsed syntax, with explicit relative-reference movement from its saved anchor.</summary>
    /// <param name="formula">Reusable parsed syntax.</param>
    /// <param name="anchor">Saved formula anchor.</param>
    /// <param name="origin">Target cell.</param>
    /// <returns>The typed value or engine diagnostics, distinguishing Excel errors from evaluation failure.</returns>
    public FormulaEvaluationResult EvaluateFormula(ParsedFormula formula, FormulaCellAddress anchor, FormulaCellAddress origin) =>
        session.Evaluate(FormulaRewriter.Copy(formula, origin.Row - anchor.Row, origin.Column - anchor.Column), origin);

    /// <summary>Reads a typed value from this session's snapshot, recalculating only in calculation mode.</summary>
    /// <param name="address">Requested source cell, including cells outside the selected output.</param>
    /// <returns>The saved or recalculated typed value; a truly absent cell is blank.</returns>
    public FormulaValue ReadValue(FormulaCellAddress address)
    {
        var cell = source.GetCell(address);
        return cell.Formula == null ? cell.Value : Evaluate(address);
    }

    /// <summary>Evaluates a source cell, retaining its style and formula metadata.</summary>
    /// <param name="cell">Source cell.</param>
    /// <returns>A snapshot containing the recalculated typed value.</returns>
    public CellRecord EvaluateCell(CellRecord cell)
    {
        if (cell == null)
        {
            throw new ArgumentNullException(nameof(cell));
        }

        if (cell.Formula == null)
        {
            return cell;
        }

        var address = new FormulaCellAddress(source.SheetNames[cell.SheetIndex], cell.Address.Row, cell.Address.Column);
        var value = Evaluate(address);
        var converted = ConvertValue(value);
        var original = cell.Formula;
        var formula = new FormulaInfo(original.RawFormula, original.Kind, original.SharedIndex, original.Reference, true, original.MasterAddress, original.MasterFormula, original.MasterReference, original.Attributes);
        return new CellRecord(cell.SheetIndex, cell.Address, cell.ExplicitStyleIndex, cell.StyleIndex, converted, null, formula, null, Array.Empty<RichTextRunInfo>(), cell.HasPhonetic);
    }

    /// <summary>Evaluates a target or throws a diagnostic exception.</summary>
    /// <param name="address">Target address.</param>
    /// <returns>The typed calculation value.</returns>
    internal FormulaValue Evaluate(FormulaCellAddress address)
    {
        var result = session.EvaluateCell(address);
        if (!result.Success)
        {
            throw new FormulaCalculationException(address, result.Diagnostics);
        }

        return result.Value;
    }

    private static CellValue ConvertValue(FormulaValue value) => value.Kind switch
    {
        FormulaValueKind.Number => new CellValue(CellValueType.Number, null, value.Number, null),
        FormulaValueKind.Text => new CellValue(CellValueType.String, value.Text, null, null),
        FormulaValueKind.Boolean => new CellValue(CellValueType.Boolean, null, null, value.Boolean),
        FormulaValueKind.Error => new CellValue(CellValueType.Error, value.Error, null, null),
        _ => new CellValue(CellValueType.Blank, null, null, null),
    };

    private sealed class Snapshot : ISparseFormulaDataSource
    {
        private readonly Dictionary<string, string> sheets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<DefinedNameInfo> names;
        private readonly FormulaRangeIndex index;
        private readonly bool recalculate;

        internal Snapshot(IWorkbookDataReader reader, CancellationToken token, bool recalculate = true)
        {
            Reader.Diagnostics.OperationMetrics.Report("calculation.snapshots", 1);
            this.recalculate = recalculate;
            names = reader.Workbook.DefinedNames;
            DateSystem = reader.Workbook.Date1904 ? FormulaDateSystem.Excel1904 : FormulaDateSystem.Excel1900;
            foreach (var sheet in reader.Sheets.Where(sheet => sheet.Kind == "Worksheet"))
            {
                sheets.Add(sheet.Name, sheet.Name);
                SheetNames.Add(sheet.Index, sheet.Name);
                foreach (var cell in reader is OpenXmlWorkbookReader openXml ? openXml.EnumerateCalculationCells(sheet.Index, token) : reader.EnumerateCells(sheet.Index, token))
                {
                    Cells.Add(new FormulaCellAddress(sheet.Name, cell.Address.Row, cell.Address.Column), cell);
                }
            }

            index = new FormulaRangeIndex(Cells.Where(pair => pair.Value.Formula != null || pair.Value.HasCalculationMetadata || pair.Value.Value.Type != CellValueType.Blank).Select(pair => pair.Key), token);
        }

        internal FormulaDateSystem DateSystem { get; }

        internal Dictionary<int, string> SheetNames { get; } = new Dictionary<int, string>();

        internal Dictionary<FormulaCellAddress, CellRecord> Cells { get; } = new Dictionary<FormulaCellAddress, CellRecord>();

        public IEnumerable<FormulaCellAddress> EnumerateStoredCells(FormulaRange range, CancellationToken cancellationToken) => index.Enumerate(range, cancellationToken);

        public bool TryResolveSheet(string name, out string sheetId) => sheets.TryGetValue(name, out sheetId!);

        public FormulaCellData GetCell(FormulaCellAddress address)
        {
            if (!Cells.TryGetValue(address, out var cell))
            {
                return FormulaCellData.Blank;
            }

            if (cell.HasCalculationMetadata)
            {
                throw new FormulaCalculationException(address.SheetId + "!" + cell.Address + ": UnsupportedSyntax: cell/value metadata, including dynamic arrays.");
            }

            if (cell.Formula != null && !recalculate && !cell.Formula.HasCachedResult)
            {
                throw new FormulaCalculationException(address + ": MissingCachedValue: formula has no saved result.");
            }

            if (cell.Formula != null && recalculate)
            {
                if (cell.Formula.Kind != "normal" && cell.Formula.Kind != string.Empty && cell.Formula.Kind != "shared")
                {
                    throw new FormulaCalculationException(address.SheetId + "!" + cell.Address + ": UnsupportedSyntax: formula kind " + cell.Formula.Kind);
                }

                if (!FormulaTextResolver.TryResolve(cell, out var text))
                {
                    throw new FormulaCalculationException(address.SheetId + "!" + cell.Address + ": UnsupportedSyntax: shared formula cannot be restored.");
                }

                return FormulaCellData.FromFormula(text!);
            }

            return FormulaCellData.FromValue(cell.Value.Type switch
            {
                CellValueType.Number => FormulaValue.FromNumber(cell.Value.Number!.Value),
                CellValueType.String => FormulaValue.FromText(cell.Value.Text ?? string.Empty),
                CellValueType.Boolean => FormulaValue.FromBoolean(cell.Value.Boolean!.Value),
                CellValueType.Error => FormulaValue.FromError(cell.Value.Text!),
                CellValueType.IsoDate => ConvertIsoDate(cell.Value.Text, address),
                _ => FormulaValue.Blank,
            });
        }

        public bool TryResolveName(string? qualifiedSheet, string name, string currentSheetId, out FormulaRange range)
        {
            range = default;
            var scope = qualifiedSheet ?? currentSheetId;
            var localIndex = SheetNames.FirstOrDefault(pair => string.Equals(pair.Value, scope, StringComparison.OrdinalIgnoreCase)).Key - 1;
            var definition = names.FirstOrDefault(item => item.LocalSheetId == localIndex && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (definition == null && qualifiedSheet == null)
            {
                definition = names.FirstOrDefault(item => item.LocalSheetId == null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            }

            if (definition == null)
            {
                return false;
            }

            var parsed = FormulaParser.Parse(definition.RawText);
            var root = parsed.Formula?.Root;
            var first = root?.Kind == FormulaNodeKind.Range ? root.Children[0] : root;
            var last = root?.Kind == FormulaNodeKind.Range ? root.Children[1] : root;
            if (first == null || last == null || first.Kind != FormulaNodeKind.Cell || last.Kind != FormulaNodeKind.Cell || !first.AbsoluteRow || !first.AbsoluteColumn || !last.AbsoluteRow || !last.AbsoluteColumn || first.Sheet == null || !TryResolveSheet(first.Sheet, out var sheet))
            {
                throw new FormulaCalculationException(scope + "!" + name + ": UnsupportedSyntax: name must contain one absolute cell or rectangular range.");
            }

            range = new FormulaRange(sheet, Math.Min(first.Row, last.Row), Math.Min(first.Column, last.Column), Math.Max(first.Row, last.Row), Math.Max(first.Column, last.Column));
            return true;
        }

        private FormulaValue ConvertIsoDate(string? text, FormulaCellAddress address)
        {
            var formats = new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF" };
            if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new FormulaCalculationException(address + ": UnsupportedSyntax: expected an ISO date without timezone.");
            }

            var epoch = DateSystem == FormulaDateSystem.Excel1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 31);
            var serial = (date - epoch).TotalDays;
            if (DateSystem == FormulaDateSystem.Excel1900 && date >= new DateTime(1900, 3, 1))
            {
                serial++;
            }

            return serial < 0 ? FormulaValue.FromError("#NUM!") : FormulaValue.FromNumber(serial);
        }
    }
}

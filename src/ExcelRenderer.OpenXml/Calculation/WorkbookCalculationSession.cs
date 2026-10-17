using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ExcelRenderer.Formulas;

namespace ExcelRenderer.OpenXml;

/// <summary>Evaluates reached cells against one immutable workbook snapshot.</summary>
public sealed class WorkbookCalculationSession
{
    private readonly Snapshot source;
    private readonly FormulaSession session;

    private WorkbookCalculationSession(Snapshot source, FormulaEvaluationOptions? options, CancellationToken token)
    {
        this.source = source;
        session = new FormulaEngine().CreateSession(source, options, token);
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

    private sealed class Snapshot : IFormulaDataSource
    {
        private readonly Dictionary<string, string> sheets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<DefinedNameInfo> names;

        internal Snapshot(IWorkbookDataReader reader, CancellationToken token)
        {
            names = reader.Workbook.DefinedNames;
            foreach (var sheet in reader.Sheets.Where(sheet => sheet.Kind == "Worksheet"))
            {
                sheets.Add(sheet.Name, sheet.Name);
                SheetNames.Add(sheet.Index, sheet.Name);
                foreach (var cell in reader is OpenXmlWorkbookReader openXml ? openXml.EnumerateCalculationCells(sheet.Index, token) : reader.EnumerateCells(sheet.Index, token))
                {
                    Cells.Add(new FormulaCellAddress(sheet.Name, cell.Address.Row, cell.Address.Column), cell);
                }
            }
        }

        internal Dictionary<int, string> SheetNames { get; } = new Dictionary<int, string>();

        internal Dictionary<FormulaCellAddress, CellRecord> Cells { get; } = new Dictionary<FormulaCellAddress, CellRecord>();

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

            if (cell.Formula != null)
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
                CellValueType.IsoDate => throw new FormulaCalculationException(address.SheetId + "!" + cell.Address + ": UnsupportedSyntax: ISO date coercion."),
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
    }
}

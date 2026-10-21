using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using ExcelRenderer.OpenXml.Reader.Model.Printing;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Connects selective recalculation to the common reader before formatting.</summary>
internal sealed partial class FullExcelReader
{
    private readonly Dictionary<int, ConditionalFormattingSession> conditionalFormatting = [];

    /// <inheritdoc/>
    protected override Func<CellRecord, CellRecord>? CreateCellValueResolver(IWorkbookDataReader reader, CancellationToken cancellationToken)
    {
        conditionalFormatting.Clear();
        var recalculate = readOptions.FormulaDisplayMode == Excel.FormulaDisplayMode.Recalculate;
        var sheets = reader.SelectedWorksheetIndices.Select(index => reader.ReadWorksheetInfo(index, cancellationToken)).ToArray();
        var hasConditionalRules = sheets.Any(info => info.ConditionalFormattingRules.Count > 0);
        if (!recalculate && !hasConditionalRules)
        {
            return null;
        }

        var calculation = recalculate ? WorkbookCalculationSession.Create(reader, readOptions.FormulaEvaluationOptions, cancellationToken) :
            hasConditionalRules && readOptions.ApplyConditionalFormatting ? WorkbookCalculationSession.CreateSavedValueSession(reader, readOptions.FormulaEvaluationOptions, cancellationToken) : null;
        var targets = recalculate ? sheets.ToDictionary(info => info.Sheet.Index, info => CreateCalculationTarget(info)) : null;
        foreach (var info in sheets.Where(info => info.ConditionalFormattingRules.Count > 0))
        {
            conditionalFormatting.Add(info.Sheet.Index, new(info, calculation, readOptions, readOptions.ApplyConditionalFormatting ? CreateCalculationTarget(info, true) : static _ => false, cancellationToken));
        }

        return !recalculate ? null : cell => cell.Formula is not null && targets![cell.SheetIndex](cell.Address) ? calculation!.EvaluateCell(cell) : cell;
    }

    /// <inheritdoc/>
    protected override void CompleteCellAppearances(WorksheetInfo info, Dictionary<Core.Model.CellAddress, Core.Model.ReportCell> cells, IReadOnlyList<Core.Model.CellRange> regions, Func<Core.Model.CellAddress, Core.Model.CellStyle> blankStyle, Core.Excel.StylePool styles, CancellationToken token)
    {
        if (conditionalFormatting.TryGetValue(info.Sheet.Index, out var session))
        {
            session.Apply(cells, regions, blankStyle, styles, token);
        }
    }

    private Func<CellAddress, bool> CreateCalculationTarget(WorksheetInfo info, bool appearance = false)
    {
        if ((appearance ? readOptions.IsConditionalFormattingTarget : readOptions.IsFormulaTarget) is { } supplied)
        {
            return address => supplied(info.Sheet.Name, address.Row, address.Column);
        }

        var selectedRegion = readOptions.GetSelectedRange?.Invoke(info.Sheet.Name);
        var mergeBands = new Core.Layout.BandIndex<OpenXml.Reader.Model.Cells.CellRange>(info.MergedRanges.Select(range => (range, (double)range.First.Row, (double)range.Last.Row + 1)));
        var titleRegions = readOptions.IgnoreFormulaPrintAreas ? Array.Empty<PrintRangeInfo>() : info.PrintReferences.Where(reference => reference.Name == "_xlnm.Print_Titles").SelectMany(reference => reference.Ranges).ToArray();
        var printRegions = readOptions.IgnoreFormulaPrintAreas ? Array.Empty<PrintRangeInfo>() : info.PrintReferences.Where(reference => reference.Name == "_xlnm.Print_Area").SelectMany(reference => reference.Ranges).ToArray();
        var hiddenRows = info.Rows.Where(row => row.Hidden ?? info.DefaultRowsHidden).Select(row => row.Index).ToHashSet();
        var visibleRows = info.Rows.Where(row => row.Hidden == false).Select(row => row.Index).ToHashSet();
        var hiddenColumns = new bool[16385];
        foreach (var column in info.Columns)
        {
            for (var index = column.Min; index <= column.Max; index++)
            {
                hiddenColumns[index] = column.Hidden ?? false;
            }
        }

        return IsCalculationTarget;

        bool IsCalculationTarget(CellAddress address)
        {
            var mergedRange = mergeBands.Query(address.Row, address.Row + 1).FirstOrDefault(range => address.Column >= range.First.Column && address.Column <= range.Last.Column);
            if (mergedRange is not null && !address.Equals(mergedRange.First))
            {
                return false;
            }

            var lastRow = mergedRange?.Last.Row ?? address.Row;
            var lastColumn = mergedRange?.Last.Column ?? address.Column;
            if (Enumerable.Range(address.Column, lastColumn - address.Column + 1).All(column => hiddenColumns[column]) ||
                Enumerable.Range(address.Row, lastRow - address.Row + 1).All(row => hiddenRows.Contains(row) || (info.DefaultRowsHidden && !visibleRows.Contains(row))))
            {
                return false;
            }

            var titleRow = titleRegions.Any(range => range.Kind == "Rows" && address.Row >= range.FirstRow && address.Row <= range.LastRow);
            var titleColumn = titleRegions.Any(range => range.Kind == "Columns" && address.Column >= range.FirstColumn && address.Column <= range.LastColumn);
            if (selectedRegion is { } region)
            {
                if ((!titleRow && (lastRow < region.FirstRow || address.Row > region.LastRow)) || (!titleColumn && (lastColumn < region.FirstColumn || address.Column > region.LastColumn)))
                {
                    return false;
                }
            }
            else if (printRegions.Length > 0 && !printRegions.Any(range => (titleRow || (lastRow >= range.FirstRow && address.Row <= range.LastRow)) && (titleColumn || (lastColumn >= range.FirstColumn && address.Column <= range.LastColumn))))
            {
                return false;
            }

            return true;
        }
    }
}

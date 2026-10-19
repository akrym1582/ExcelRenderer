using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DocumentFormat.OpenXml.Packaging;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Reader;
using ExcelRenderer.OpenXml.Reader.Input;
using ExcelRenderer.OpenXml.Reader.Model.Cells;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace ExcelRenderer.OpenXml.Calculation;

/// <summary>Stages explicit calculation and persists typed caches without modifying the input.</summary>
public static class WorkbookRecalculator
{
    /// <summary>Recalculates selected targets and their dependencies, or all formula cells.</summary>
    /// <param name="input">Seekable XLSX source, left open.</param>
    /// <param name="output">Distinct writable output, untouched until calculation succeeds.</param>
    /// <param name="targets">Optional cell targets; null means all formulas.</param>
    /// <param name="options">Calculation resource limits.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <remarks>All unevaluated formula caches are removed. A failing final destination write can be partial.</remarks>
    public static void Recalculate(Stream input, Stream output, IEnumerable<FormulaCellAddress>? targets = null, FormulaEvaluationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (input == null || output == null)
        {
            throw new ArgumentNullException(input == null ? nameof(input) : nameof(output));
        }

        if (ReferenceEquals(input, output) || !input.CanSeek || !output.CanWrite)
        {
            throw new ArgumentException("Input must be seekable and output must be a distinct writable stream.");
        }

        input.Position = 0;
        WorkbookCalculationSession session;
        using (var reader = OpenXmlWorkbookReader.Open(input))
        {
            session = WorkbookCalculationSession.Create(reader, options, cancellationToken);
        }

        foreach (var target in targets ?? session.FormulaCells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.Evaluate(target);
        }

        var formulaCells = new HashSet<FormulaCellAddress>(session.FormulaCells);
        input.Position = 0;
        using var stage = new SpillableBufferStream(new InputBufferOptions());
        input.CopyTo(stage);
        stage.Position = 0;
        using (var document = SpreadsheetDocument.Open(stage, true))
        {
            var workbook = document.WorkbookPart!;
            foreach (var sheet in workbook.Workbook.Sheets!.Elements<S.Sheet>())
            {
                if (!(workbook.GetPartById(sheet.Id!.Value!) is WorksheetPart part))
                {
                    continue;
                }

                foreach (var cell in part.Worksheet.Descendants<S.Cell>())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var coordinate = CellAddress.Parse(cell.CellReference!.Value!);
                    var address = new FormulaCellAddress(sheet.Name!.Value!, coordinate.Row, coordinate.Column);
                    if (!formulaCells.Contains(address))
                    {
                        continue;
                    }

                    cell.CellValue = null;
                    cell.InlineString = null;
                    cell.DataType = null;
                    if (session.EvaluatedCells.TryGetValue(address, out var result) && result.Success)
                    {
                        WriteValue(cell, result.Value);
                    }
                }

                part.Worksheet.Save();
            }

            if (workbook.CalculationChainPart != null)
            {
                workbook.DeletePart(workbook.CalculationChainPart);
            }

            workbook.Workbook.CalculationProperties = new S.CalculationProperties { FullCalculationOnLoad = true, ForceFullCalculation = true };
            workbook.Workbook.Save();
        }

        cancellationToken.ThrowIfCancellationRequested();
        stage.Position = 0;
        stage.CopyTo(output);
    }

    private static void WriteValue(S.Cell cell, FormulaValue value)
    {
        switch (value.Kind)
        {
            case FormulaValueKind.Number:
                cell.CellValue = new S.CellValue(value.Number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case FormulaValueKind.Text:
                cell.DataType = S.CellValues.String;
                cell.CellValue = new S.CellValue(value.Text);
                break;
            case FormulaValueKind.Boolean:
                cell.DataType = S.CellValues.Boolean;
                cell.CellValue = new S.CellValue(value.Boolean ? "1" : "0");
                break;
            case FormulaValueKind.Error:
                cell.DataType = S.CellValues.Error;
                cell.CellValue = new S.CellValue(value.Error);
                break;
        }
    }
}

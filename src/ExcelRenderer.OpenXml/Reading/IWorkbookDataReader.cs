using System;
using System.Collections.Generic;
using System.Threading;
using ExcelRenderer.OpenXml.Formatting;
using ExcelRenderer.OpenXml.Model.Cells;
using ExcelRenderer.OpenXml.Model.Styles;
using ExcelRenderer.OpenXml.Model.Workbook;

namespace ExcelRenderer.OpenXml.Reading;

/// <summary>Source-data boundary for future rendering adapters, with no SDK or rendering model types.</summary>
public interface IWorkbookDataReader : IDisposable
{
    /// <summary>Gets the workbook's date system, names and source font/theme information.</summary>
    WorkbookInfo Workbook { get; }

    /// <summary>Gets every original sheet, preserving its workbook position and visibility.</summary>
    IReadOnlyList<SheetInfo> Sheets { get; }

    /// <summary>Gets selected worksheet positions in workbook order, excluding chart sheets.</summary>
    IReadOnlyList<int> SelectedWorksheetIndices { get; }

    /// <summary>Reads source-unit geometry and print metadata, including unselected worksheets on explicit request.</summary>
    /// <param name="sheetIndex">Original one-based workbook position.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Immutable sparse worksheet metadata.</returns>
    WorksheetInfo ReadWorksheetInfo(int sheetIndex, CancellationToken cancellationToken = default);

    /// <summary>Streams immutable XML-present cells; only one worksheet operation may be active.</summary>
    /// <param name="sheetIndex">Original one-based selected worksheet position.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Cell snapshots without materializing missing cells.</returns>
    IEnumerable<CellRecord> EnumerateCells(int sheetIndex, CancellationToken cancellationToken = default);

    /// <summary>Resolves a workbook-local style ID from a cell, row, column or the default style.</summary>
    /// <param name="styleIndex">Effective zero-based cell-XF index.</param>
    /// <returns>Shared source style metadata; conversion caches must also account for value type and product policy.</returns>
    CellStyleInfo ResolveStyle(int styleIndex);

    /// <summary>Resolves a cell's effective source style.</summary>
    /// <param name="cell">A snapshot from this workbook.</param>
    /// <returns>Shared source style metadata.</returns>
    CellStyleInfo ResolveStyle(CellRecord cell);

    /// <summary>Formats stored values, leaving text normalization and geometry to the consuming adapter.</summary>
    /// <param name="cell">A snapshot from this workbook.</param>
    /// <returns>Display text and call-specific diagnostics, without formula evaluation.</returns>
    CellDisplayResult FormatCell(CellRecord cell);
}

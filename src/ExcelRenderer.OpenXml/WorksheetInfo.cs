using System.Collections.Generic;
using System.Globalization;

namespace ExcelRenderer.OpenXml;

/// <summary>Immutable WorksheetInfo snapshot. XML attribute names and source units are preserved.</summary>
public sealed class WorksheetInfo
{
    /// <summary>Initializes a new instance of the <see cref="WorksheetInfo"/> class.</summary>
    /// <param name="sheet">The sheet value.</param>
    /// <param name="rawDimension">The rawDimension value.</param>
    /// <param name="actualCellRange">The actualCellRange value.</param>
    /// <param name="rows">The rows value.</param>
    /// <param name="columns">The columns value.</param>
    /// <param name="mergedRanges">The mergedRanges value.</param>
    /// <param name="sheetFormat">The sheetFormat value.</param>
    /// <param name="pageSetup">The pageSetup value.</param>
    /// <param name="pageSetupProperties">The pageSetupProperties value.</param>
    /// <param name="pageMargins">The pageMargins value.</param>
    /// <param name="printOptions">The printOptions value.</param>
    /// <param name="headerFooter">The headerFooter value.</param>
    /// <param name="rowBreaks">The rowBreaks value.</param>
    /// <param name="columnBreaks">The columnBreaks value.</param>
    /// <param name="printReferences">The printReferences value.</param>
    /// <param name="hasDrawings">The hasDrawings value.</param>
    /// <param name="hasHyperlinks">The hasHyperlinks value.</param>
    /// <param name="hasConditionalFormatting">The hasConditionalFormatting value.</param>
    /// <param name="hasTables">The hasTables value.</param>
    /// <param name="conditionalFormattingRanges">Source rule ranges.</param>
    /// <param name="hasFormulas">Whether any original cell has a formula element.</param>
    internal WorksheetInfo(SheetInfo sheet, string? rawDimension, CellRange? actualCellRange, IReadOnlyList<RowInfo> rows, IReadOnlyList<ColumnInfo> columns, IReadOnlyList<CellRange> mergedRanges, ElementInfo? sheetFormat, ElementInfo? pageSetup, ElementInfo? pageSetupProperties, ElementInfo? pageMargins, ElementInfo? printOptions, ElementInfo? headerFooter, IReadOnlyList<ElementInfo> rowBreaks, IReadOnlyList<ElementInfo> columnBreaks, IReadOnlyList<PrintReferenceInfo> printReferences, bool hasDrawings, bool hasHyperlinks, bool hasConditionalFormatting, bool hasTables, IReadOnlyList<IReadOnlyList<CellRange>> conditionalFormattingRanges, bool hasFormulas)
    {
        Sheet = sheet;
        RawDimension = rawDimension;
        ActualCellRange = actualCellRange;
        Rows = rows;
        Columns = columns;
        MergedRanges = mergedRanges;
        SheetFormat = sheetFormat;
        PageSetup = pageSetup;
        PageSetupProperties = pageSetupProperties;
        PageMargins = pageMargins;
        PrintOptions = printOptions;
        HeaderFooter = headerFooter;
        RowBreaks = rowBreaks;
        ColumnBreaks = columnBreaks;
        PrintReferences = printReferences;
        HasDrawings = hasDrawings;
        HasHyperlinks = hasHyperlinks;
        HasConditionalFormatting = hasConditionalFormatting;
        HasTables = hasTables;
        ConditionalFormattingRanges = conditionalFormattingRanges;
        HasFormulas = hasFormulas;
    }

    /// <summary>Gets the Sheet metadata.</summary>
    public SheetInfo Sheet { get; }

    /// <summary>Gets the untrusted dimension hint; no cells are synthesized from this range.</summary>
    public string? RawDimension { get; }

    /// <summary>Gets the bounds of XML-present cells, or null for an empty worksheet.</summary>
    public CellRange? ActualCellRange { get; }

    /// <summary>Gets the Rows metadata.</summary>
    public IReadOnlyList<RowInfo> Rows { get; }

    /// <summary>Gets the Columns metadata.</summary>
    public IReadOnlyList<ColumnInfo> Columns { get; }

    /// <summary>Gets the MergedRanges metadata.</summary>
    public IReadOnlyList<CellRange> MergedRanges { get; }

    /// <summary>Gets the saved sheetFormatPr attributes, preserving omitted values and explicit zero.</summary>
    public ElementInfo? SheetFormat { get; }

    /// <summary>Gets the PageSetup metadata.</summary>
    public ElementInfo? PageSetup { get; }

    /// <summary>Gets the saved fitToPage and related sheet properties, separate from scale.</summary>
    public ElementInfo? PageSetupProperties { get; }

    /// <summary>Gets the saved margin attributes in inches.</summary>
    public ElementInfo? PageMargins { get; }

    /// <summary>Gets the PrintOptions metadata.</summary>
    public ElementInfo? PrintOptions { get; }

    /// <summary>Gets the original header and footer text, without expanding control codes.</summary>
    public ElementInfo? HeaderFooter { get; }

    /// <summary>Gets the RowBreaks metadata.</summary>
    public IReadOnlyList<ElementInfo> RowBreaks { get; }

    /// <summary>Gets the ColumnBreaks metadata.</summary>
    public IReadOnlyList<ElementInfo> ColumnBreaks { get; }

    /// <summary>Gets the PrintReferences metadata.</summary>
    public IReadOnlyList<PrintReferenceInfo> PrintReferences { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool HasDrawings { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool HasHyperlinks { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool HasConditionalFormatting { get; }

    /// <summary>Gets a value indicating whether this flag is set.</summary>
    public bool HasTables { get; }

    /// <summary>Gets a value indicating whether any source cell contains a formula element.</summary>
    public bool HasFormulas { get; }

    /// <summary>Gets original conditional-format rule source ranges, without retaining rule bodies.</summary>
    public IReadOnlyList<IReadOnlyList<CellRange>> ConditionalFormattingRanges { get; }

    /// <summary>Gets the saved default row height in points, or the reader default of 15 when absent.</summary>
    public double DefaultRowHeightPt => SheetFormat != null && SheetFormat.Attributes.TryGetValue("defaultRowHeight", out var value) ? double.Parse(value, CultureInfo.InvariantCulture) : 15;

    /// <summary>Gets the saved default column width in Excel units, or null when not supplied.</summary>
    public double? DefaultColumnWidthExcel => SheetFormat != null && SheetFormat.Attributes.TryGetValue("defaultColWidth", out var value) ? double.Parse(value, CultureInfo.InvariantCulture) : (double?)null;

    /// <summary>Gets the saved base column character count, defaulting to the OOXML value 8.</summary>
    public int BaseColumnWidthCharacters => SheetFormat != null && SheetFormat.Attributes.TryGetValue("baseColWidth", out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : 8;

    /// <summary>Gets a value indicating whether rows without explicit heights are hidden by default.</summary>
    public bool DefaultRowsHidden => SheetFormat != null && SheetFormat.Attributes.TryGetValue("zeroHeight", out var value) && (value == "1" || value == "true");
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace ExcelRenderer.Mapping;

/// <summary>Validates marker structure and compiles all template expressions before expansion.</summary>
internal sealed class TemplateSheet
{
    /// <summary>Initializes a new instance of the <see cref="TemplateSheet"/> class.</summary>
    /// <param name="sheet">The sheet.</param>
    internal TemplateSheet(IXLWorksheet sheet)
    {
        Sheet = sheet;
        LastRow = sheet.LastRowUsed(XLCellsUsedOptions.All)?.RowNumber() ?? 0;
        LastColumn = sheet.LastColumnUsed(XLCellsUsedOptions.All)?.ColumnNumber() ?? 1;
        var row = 1;
        Nodes = Parse(ref row, new HashSet<string>(StringComparer.Ordinal), false);
    }

    /// <summary>Gets the template worksheet.</summary>
    internal IXLWorksheet Sheet { get; }

    /// <summary>Gets the last used template row.</summary>
    internal int LastRow { get; }

    /// <summary>Gets the last used template column.</summary>
    internal int LastColumn { get; }

    /// <summary>Gets the compiled template regions.</summary>
    internal List<TemplateNode> Nodes { get; }

    /// <summary>Runs a cell operation with original-template diagnostics.</summary>
    /// <typeparam name="T">The operation result type.</typeparam>
    /// <param name="sheet">The template worksheet.</param>
    /// <param name="cell">The original cell.</param>
    /// <param name="action">The cell operation.</param>
    /// <returns>The operation result.</returns>
    internal static T At<T>(IXLWorksheet sheet, TemplateCell cell, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception) when (exception is not MappingException && exception is not OperationCanceledException)
        {
            throw new MappingException(sheet.Name, cell.Address, cell.Text, exception.Message, exception);
        }
    }

    private List<TemplateNode> Parse(ref int row, HashSet<string> aliases, bool insideArray)
    {
        var nodes = new List<TemplateNode>();
        while (row <= LastRow)
        {
            var sourceRow = row;
            var cells = Sheet.Row(row).CellsUsed(XLCellsUsedOptions.Contents).Where(c => !c.HasFormula && c.DataType == XLDataType.Text)
                .Select(c => new TemplateCell(c.Address.ToStringRelative(), c.Address.ColumnNumber, c.GetString())).ToArray();
            var marker = cells.FirstOrDefault(c => c.Text.StartsWith("**@start-array", StringComparison.Ordinal) || c.Text.StartsWith("**@end-array", StringComparison.Ordinal));
            if (marker is not null)
            {
                if (Sheet.Row(row).CellsUsed(XLCellsUsedOptions.Contents).Count() != 1 || Sheet.MergedRanges.Any(r => r.RangeAddress.FirstAddress.RowNumber <= sourceRow && r.RangeAddress.LastAddress.RowNumber >= sourceRow))
                {
                    throw Error(marker, "An array marker requires a dedicated, unmerged row with no other content.");
                }

                if (marker.Text == "**@end-array")
                {
                    if (!insideArray)
                    {
                        throw Error(marker, "An end-array marker has no matching start-array.");
                    }

                    return nodes;
                }

                var match = Regex.Match(marker.Text, @"^\*\*@start-array\s+(.+?)\s+as\s+([\p{L}_][\p{L}\p{N}_]*)$");
                if (!match.Success)
                {
                    throw Error(marker, "Expected **@start-array path[*] as alias.");
                }

                var path = At(Sheet, marker, () => new DataPath(match.Groups[1].Value));
                ValidateAlias(marker, path, aliases);
                if (!path.EndsInWildcard)
                {
                    throw Error(marker, "A start-array path must end with [*].");
                }

                var alias = match.Groups[2].Value;
                if (aliases.Contains(alias))
                {
                    throw Error(marker, "An array alias cannot shadow an enclosing alias.");
                }

                var nestedAliases = new HashSet<string>(aliases, StringComparer.Ordinal) { alias };
                row++;
                var children = Parse(ref row, nestedAliases, true);
                if (row > LastRow)
                {
                    throw Error(marker, "The start-array marker has no matching end-array.");
                }

                var node = new TemplateNode(sourceRow, row, marker, path, alias, children);
                ValidateRepeatedRange(node);
                nodes.Add(node);
                row++;
                continue;
            }

            var ordinary = new TemplateNode(row, row, cells.FirstOrDefault() ?? new TemplateCell($"A{row}", 1, string.Empty));
            foreach (var cell in cells)
            {
                if (cell.Text == "**@page-break")
                {
                    ordinary.Cells.Add(cell);
                }
                else if (cell.Text.StartsWith("\\**", StringComparison.Ordinal))
                {
                    ordinary.Cells.Add(cell);
                }
                else if (cell.Text.StartsWith("**", StringComparison.Ordinal))
                {
                    cell.Expression = At(Sheet, cell, () => new CellExpression(cell.Text.Substring(2)));
                    ValidateAlias(cell, cell.Expression.Path, aliases);
                    if (cell.Expression.Path.WildcardIndex >= 0)
                    {
                        if (insideArray)
                        {
                            throw Error(cell, "Use nested start-array/end-array markers inside an array block.");
                        }

                        if (ordinary.Path is not null && ordinary.Path.ArrayKey != cell.Expression.Path.ArrayKey)
                        {
                            throw Error(cell, "All wildcard cells in one row must refer to the same array.");
                        }

                        ordinary.Path = cell.Expression.Path;
                        ordinary.Location = cell;
                    }

                    ordinary.Cells.Add(cell);
                }
            }

            if (ordinary.Path is not null)
            {
                ValidateRepeatedRange(ordinary);
            }

            nodes.Add(ordinary);
            row++;
        }

        return nodes;
    }

    private void ValidateAlias(TemplateCell cell, DataPath path, HashSet<string> aliases)
    {
        if (path.Alias is not null && !aliases.Contains(path.Alias))
        {
            throw Error(cell, $"Array alias '{path.Alias}' is not in scope.");
        }
    }

    private void ValidateRepeatedRange(TemplateNode node)
    {
        var first = node.IsBlock ? node.FirstRow + 1 : node.FirstRow;
        var last = node.IsBlock ? node.LastRow - 1 : node.LastRow;
        foreach (var merged in Sheet.MergedRanges)
        {
            var range = merged.RangeAddress;
            if (range.FirstAddress.RowNumber <= last && range.LastAddress.RowNumber >= first &&
                (range.FirstAddress.RowNumber < first || range.LastAddress.RowNumber > last))
            {
                throw Error(node.Location, "A merged range crosses a repeated region boundary.");
            }
        }

        if (Sheet.Tables.Any(t => t.RangeAddress.FirstAddress.RowNumber <= node.LastRow && t.RangeAddress.LastAddress.RowNumber >= node.FirstRow))
        {
            throw Error(node.Location, "Excel tables overlapping repeated regions are not supported; use ordinary cells.");
        }

        if (Sheet.Pictures.Any(p => p.TopLeftCell.Address.RowNumber <= node.LastRow && p.BottomRightCell.Address.RowNumber >= node.FirstRow))
        {
            throw Error(node.Location, "Pictures overlapping repeated regions are not supported.");
        }
    }

    private MappingException Error(TemplateCell cell, string message) => new MappingException(Sheet.Name, cell.Address, cell.Text, message);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ExcelRenderer.Mapping.Diagnostics;
using ExcelRenderer.Mapping.Expressions;

namespace ExcelRenderer.Mapping.Templates;

/// <summary>Validates marker structure and compiles all template expressions before expansion.</summary>
internal sealed class TemplateSheet
{
    /// <summary>Initializes a new instance of the <see cref="TemplateSheet"/> class.</summary>
    /// <param name="sheet">The sheet.</param>
    internal TemplateSheet(TemplateSnapshot sheet)
    {
        Sheet = sheet;
        LastRow = sheet.LastRow;
        LastColumn = sheet.LastColumn;
        var row = 1;
        Nodes = Parse(ref row, new HashSet<string>(StringComparer.Ordinal), false, null);
    }

    /// <summary>Gets the template worksheet.</summary>
    internal TemplateSnapshot Sheet { get; }

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
    internal static T At<T>(TemplateSnapshot sheet, TemplateCell cell, Func<T> action)
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

    /// <summary>Validates repeated-region restrictions after every sheet has compiled its syntax.</summary>
    internal void ValidateRepeatedRanges() => Validate(Nodes);

    private List<TemplateNode> Parse(ref int row, HashSet<string> aliases, bool insideArray, string? endMarker)
    {
        var nodes = new List<TemplateNode>();
        while (row <= LastRow)
        {
            var sourceRow = row;
            var cells = Sheet.TextCells.TryGetValue(row, out var sourceCells) ? sourceCells : Array.Empty<TemplateCell>();
            var marker = cells.FirstOrDefault(c => IsBlockMarker(NormalizeDirective(c.Text)));
            if (marker is not null)
            {
                var directive = NormalizeDirective(marker.Text);
                var conditional = IsDirective(directive, "**@if") || IsDirective(directive, "**@endif");
                if (Sheet.ContentCounts[sourceRow] != 1 || Sheet.MergedRanges.Any(r => r.First.Row <= sourceRow && r.Last.Row >= sourceRow))
                {
                    throw Error(marker, conditional
                        ? "A conditional marker requires a dedicated, unmerged row with no other content."
                        : "An array marker requires a dedicated, unmerged row with no other content.");
                }

                if (directive == "**@end-array" || IsDirective(directive, "**@endif"))
                {
                    if (directive != "**@end-array" && directive != "**@endif")
                    {
                        throw Error(marker, "Expected **@endif without arguments.");
                    }

                    if (directive != endMarker)
                    {
                        throw Error(marker, directive == "**@endif"
                            ? "An endif marker has no matching if at this nesting level."
                            : "An end-array marker has no matching start-array at this nesting level.");
                    }

                    return nodes;
                }

                if (conditional)
                {
                    var condition = At(Sheet, marker, () => new MappingCondition(directive.Substring(5)));
                    foreach (var path in condition.Paths)
                    {
                        ValidateAlias(marker, path, aliases);
                    }

                    row++;
                    var body = Parse(ref row, aliases, insideArray, "**@endif");
                    if (row > LastRow)
                    {
                        throw Error(marker, "The if marker has no matching endif.");
                    }

                    nodes.Add(new TemplateNode(sourceRow, row, marker, children: body) { Condition = condition });
                    row++;
                    continue;
                }

                var match = Regex.Match(directive, @"^\*\*@start-array\s+(.+?)\s+as\s+([\p{L}_][\p{L}\p{N}_]*)(?:\s*,\s*([\p{L}_][\p{L}\p{N}_]*))?$");
                if (!match.Success)
                {
                    throw Error(marker, "Expected **@start-array path[*] as alias[, index].");
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
                var indexAlias = match.Groups[3].Success ? match.Groups[3].Value : null;
                if (indexAlias is not null && !nestedAliases.Add(indexAlias))
                {
                    throw Error(marker, "An index alias cannot duplicate or shadow an array alias.");
                }

                row++;
                var children = Parse(ref row, nestedAliases, true, "**@end-array");
                if (row > LastRow)
                {
                    throw Error(marker, "The start-array marker has no matching end-array.");
                }

                var node = new TemplateNode(sourceRow, row, marker, path, alias, children, indexAlias);
                nodes.Add(node);
                row++;
                continue;
            }

            var ordinary = new TemplateNode(row, row, cells.FirstOrDefault() ?? new TemplateCell($"A{row}", 1, string.Empty));
            foreach (var cell in cells)
            {
                var text = NormalizeDirective(cell.Text);
                if (text.StartsWith("**//", StringComparison.Ordinal))
                {
                    cell.IsComment = true;
                    ordinary.Cells.Add(cell);
                }
                else if (text == "**@page-break" || text.StartsWith("**@page-break ", StringComparison.Ordinal))
                {
                    cell.IsPageBreak = true;
                    if (text != "**@page-break")
                    {
                        cell.Condition = At(Sheet, cell, () => new MappingCondition(text, pageBreak: true));
                        foreach (var path in cell.Condition.Paths)
                        {
                            ValidateAlias(cell, path, aliases);
                        }
                    }

                    ordinary.Cells.Add(cell);
                }
                else if (text.StartsWith("\\**", StringComparison.Ordinal))
                {
                    ordinary.Cells.Add(cell);
                }
                else if (text == "**@image" || text.StartsWith("**@image ", StringComparison.Ordinal))
                {
                    cell.Image = At(Sheet, cell, () => new ImageExpression(text.Substring(8)));
                    foreach (var path in cell.Image.Paths)
                    {
                        ValidateAlias(cell, path, aliases);
                        if (path.WildcardIndex >= 0)
                        {
                            if (insideArray || (ordinary.Path is not null && ordinary.Path.ArrayKey != path.ArrayKey))
                            {
                                throw Error(cell, "Image wildcards must match the row array; use aliases inside blocks.");
                            }

                            ordinary.Path = path;
                            ordinary.Location = cell;
                        }
                    }

                    ordinary.Cells.Add(cell);
                }
                else if (text.StartsWith("**", StringComparison.Ordinal))
                {
                    cell.Expression = At(Sheet, cell, () => new CellExpression(text.Substring(2)));
                    ValidateAlias(cell, cell.Expression.Path, aliases);
                    foreach (var path in cell.Expression.DecorationPaths)
                    {
                        ValidateAlias(cell, path, aliases);
                        if (path.WildcardIndex >= 0 && path.ArrayKey != cell.Expression.Path.ArrayKey)
                        {
                            throw Error(cell, "Decoration wildcards must match the cell value array.");
                        }
                    }

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

            nodes.Add(ordinary);
            row++;
        }

        return nodes;
    }

    private static string NormalizeDirective(string text)
    {
        var position = 3;
        if (!text.StartsWith("**@ ", StringComparison.Ordinal))
        {
            return text;
        }

        while (position < text.Length && text[position] == ' ')
        {
            position++;
        }

        return "**@" + text.Substring(position);
    }

    private static bool IsDirective(string text, string marker) =>
        text.StartsWith(marker, StringComparison.Ordinal) &&
        (text.Length == marker.Length || char.IsWhiteSpace(text[marker.Length]));

    private static bool IsBlockMarker(string text) =>
        text.StartsWith("**@start-array", StringComparison.Ordinal) ||
        text.StartsWith("**@end-array", StringComparison.Ordinal) ||
        IsDirective(text, "**@if") || IsDirective(text, "**@endif");

    private void ValidateAlias(TemplateCell cell, DataPath path, HashSet<string> aliases)
    {
        if (path.Alias is not null && !aliases.Contains(path.Alias))
        {
            throw Error(cell, $"Array alias '{path.Alias}' is not in scope.");
        }
    }

    private void Validate(List<TemplateNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.Path is not null || node.Condition is not null)
            {
                Sheet.ValidateRepeatedRange(node);
            }

            if (node.Children is not null)
            {
                Validate(node.Children);
            }
        }
    }

    private MappingException Error(TemplateCell cell, string message) => new MappingException(Sheet.Name, cell.Address, cell.Text, message);
}

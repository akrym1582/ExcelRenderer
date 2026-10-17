using System;
using System.Collections.Generic;
using System.Linq;

namespace ExcelRenderer.Formulas;

/// <summary>An immutable formula syntax node.</summary>
public sealed class FormulaNode
{
    /// <summary>Initializes a new instance of the <see cref="FormulaNode"/> class.</summary>
    /// <param name="kind">The kind value.</param>
    /// <param name="span">The span value.</param>
    /// <param name="text">The text value.</param>
    /// <param name="number">The number value.</param>
    /// <param name="boolean">The boolean value.</param>
    /// <param name="children">The children value.</param>
    /// <param name="sheet">The sheet value.</param>
    /// <param name="row">The row value.</param>
    /// <param name="column">The column value.</param>
    /// <param name="absoluteRow">The absoluteRow value.</param>
    /// <param name="absoluteColumn">The absoluteColumn value.</param>
    internal FormulaNode(FormulaNodeKind kind, SourceSpan span, string text = "", double number = 0, bool boolean = false, IEnumerable<FormulaNode>? children = null, string? sheet = null, int row = 0, int column = 0, bool absoluteRow = false, bool absoluteColumn = false)
    {
        Kind = kind;
        Span = span;
        Text = text;
        Number = number;
        Boolean = boolean;
        Children = Array.AsReadOnly(children?.ToArray() ?? Array.Empty<FormulaNode>());
        Sheet = sheet;
        Row = row;
        Column = column;
        AbsoluteRow = absoluteRow;
        AbsoluteColumn = absoluteColumn;
    }

    /// <summary>Gets the node kind.</summary>
    public FormulaNodeKind Kind { get; }

    /// <summary>Gets the original UTF-16 span.</summary>
    public SourceSpan Span { get; }

    /// <summary>Gets the literal, operator or function name.</summary>
    public string Text { get; }

    /// <summary>Gets the numeric literal.</summary>
    public double Number { get; }

    /// <summary>Gets a value indicating whether the Boolean literal is true.</summary>
    public bool Boolean { get; }

    /// <summary>Gets the immutable children.</summary>
    public IReadOnlyList<FormulaNode> Children { get; }

    /// <summary>Gets the optional sheet qualifier.</summary>
    public string? Sheet { get; }

    /// <summary>Gets the one-based row, or zero for an entire column.</summary>
    public int Row { get; }

    /// <summary>Gets the one-based column, or zero for an entire row.</summary>
    public int Column { get; }

    /// <summary>Gets a value indicating whether the row is absolute for copying.</summary>
    public bool AbsoluteRow { get; }

    /// <summary>Gets a value indicating whether the column is absolute for copying.</summary>
    public bool AbsoluteColumn { get; }

    /// <summary>Returns a reference with transformed coordinates.</summary>
    /// <param name="sheet">Sheet qualifier.</param>
    /// <param name="row">Row coordinate.</param>
    /// <param name="column">Column coordinate.</param>
    /// <param name="absoluteRow">Absolute row flag.</param>
    /// <param name="absoluteColumn">Absolute column flag.</param>
    /// <returns>The transformed node.</returns>
    public FormulaNode WithReference(string? sheet, int row, int column, bool absoluteRow, bool absoluteColumn)
    {
        return new FormulaNode(Kind, Span, Text, Number, Boolean, Children, sheet, row, column, absoluteRow, absoluteColumn);
    }

    /// <summary>Returns a node with replacement children.</summary>
    /// <param name="children">Replacement children.</param>
    /// <returns>The transformed node.</returns>
    public FormulaNode WithChildren(IEnumerable<FormulaNode> children)
    {
        return new FormulaNode(Kind, Span, Text, Number, Boolean, children, Sheet, Row, Column, AbsoluteRow, AbsoluteColumn);
    }
}

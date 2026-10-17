using System;

namespace ExcelRenderer.OpenXml.Writing.Plans;

/// <summary>A typed cell replacement; strings are never interpreted as formulas.</summary>
public sealed class CellValueEdit
{
    /// <summary>Initializes a new instance of the <see cref="CellValueEdit"/> class.</summary>
    /// <param name="column">One-based source and output column.</param>
    /// <param name="value">Typed replacement value; null clears the stored value.</param>
    public CellValueEdit(int column, string? value)
    {
        Column = column;
        Value = value;
    }

    /// <summary>Initializes a new instance of the <see cref="CellValueEdit"/> class.</summary>
    /// <param name="column">One-based source and output column.</param>
    /// <param name="value">Typed replacement value; null clears the stored value.</param>
    public CellValueEdit(int column, double value)
    {
        Column = column;
        Value = value;
    }

    /// <summary>Initializes a new instance of the <see cref="CellValueEdit"/> class.</summary>
    /// <param name="column">One-based source and output column.</param>
    /// <param name="value">Typed replacement value; null clears the stored value.</param>
    public CellValueEdit(int column, bool value)
    {
        Column = column;
        Value = value;
    }

    /// <summary>Initializes a new instance of the <see cref="CellValueEdit"/> class.</summary>
    /// <param name="column">One-based source and output column.</param>
    /// <param name="value">Typed replacement value; null clears the stored value.</param>
    public CellValueEdit(int column, DateTime value)
    {
        Column = column;
        Value = value;
    }

    /// <summary>Initializes a new instance of the <see cref="CellValueEdit"/> class.</summary>
    /// <param name="column">One-based source and output column.</param>
    /// <param name="value">Typed replacement value; null clears the stored value.</param>
    public CellValueEdit(int column, TimeSpan value)
    {
        Column = column;
        Value = value;
    }

    /// <summary>Gets the one-based cell column.</summary>
    public int Column { get; }

    /// <summary>Gets a null, string, double, boolean, DateTime or TimeSpan value.</summary>
    public object? Value { get; }
}

namespace ExcelRenderer.Mapping;

/// <summary>An original cell location and its optional compiled expression.</summary>
internal sealed class TemplateCell
{
    /// <summary>Initializes a new instance of the <see cref="TemplateCell"/> class.</summary>
    /// <param name="address">The address.</param>
    /// <param name="column">The column.</param>
    /// <param name="text">The text.</param>
    internal TemplateCell(string address, int column, string text)
    {
        Address = address;
        Column = column;
        Text = text;
    }

    /// <summary>Gets the original A1 address.</summary>
    internal string Address { get; }

    /// <summary>Gets the original column number.</summary>
    internal int Column { get; }

    /// <summary>Gets the original expression text.</summary>
    internal string Text { get; }

    /// <summary>Gets or sets the optional compiled expression.</summary>
    internal CellExpression? Expression { get; set; }
}

namespace ExcelRenderer.Formulas;

/// <summary>An immutable parsed formula.</summary>
public sealed class ParsedFormula
{
    /// <summary>Initializes a new instance of the <see cref="ParsedFormula"/> class.</summary>
    /// <param name="text">The text value.</param>
    /// <param name="root">The root value.</param>
    internal ParsedFormula(string text, FormulaNode root)
    {
        Text = text;
        Root = root;
    }

    /// <summary>Gets the unmodified source.</summary>
    public string Text { get; }

    /// <summary>Gets the syntax root.</summary>
    public FormulaNode Root { get; }
}

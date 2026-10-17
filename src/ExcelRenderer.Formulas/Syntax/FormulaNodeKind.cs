namespace ExcelRenderer.Formulas;

/// <summary>Identifies a formula syntax node.</summary>
public enum FormulaNodeKind
{
    /// <summary>Number syntax.</summary>
    Number,

    /// <summary>Text syntax.</summary>
    Text,

    /// <summary>Boolean syntax.</summary>
    Boolean,

    /// <summary>Error syntax.</summary>
    Error,

    /// <summary>Unary syntax.</summary>
    Unary,

    /// <summary>Binary syntax.</summary>
    Binary,

    /// <summary>Percent syntax.</summary>
    Percent,

    /// <summary>Function syntax.</summary>
    Function,

    /// <summary>Missing syntax.</summary>
    Missing,

    /// <summary>Cell syntax.</summary>
    Cell,

    /// <summary>Range syntax.</summary>
    Range,

    /// <summary>Name syntax.</summary>
    Name,

    /// <summary>Parenthesized syntax.</summary>
    Parenthesized,
}

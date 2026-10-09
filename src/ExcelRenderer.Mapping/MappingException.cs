using System;

namespace ExcelRenderer.Mapping;

/// <summary>Identifies a mapping failure in the original template.</summary>
public sealed class MappingException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="MappingException"/> class.</summary>
    /// <param name="sheetName">The original worksheet name.</param>
    /// <param name="cellAddress">The original cell address.</param>
    /// <param name="expression">The template expression.</param>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The underlying failure.</param>
    public MappingException(string sheetName, string cellAddress, string expression, string message, Exception? innerException = null)
        : base($"{sheetName}!{cellAddress} [{expression}]: {message}", innerException)
    {
        SheetName = sheetName;
        CellAddress = cellAddress;
        Expression = expression;
    }

    /// <summary>Gets the original worksheet name.</summary>
    public string SheetName { get; }

    /// <summary>Gets the original cell address.</summary>
    public string CellAddress { get; }

    /// <summary>Gets the template expression.</summary>
    public string Expression { get; }
}

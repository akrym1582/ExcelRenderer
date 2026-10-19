using System.Collections.Generic;
using ExcelRenderer.OpenXml.Reader.Model.Cells;

namespace ExcelRenderer.OpenXml.Reader.Diagnostics;

/// <summary>Immutable ReaderDiagnostic snapshot. XML attribute names and source units are preserved.</summary>
public sealed class ReaderDiagnostic
{
    /// <summary>Initializes a new instance of the <see cref="ReaderDiagnostic"/> class.</summary>
    /// <param name="code">The code value.</param>
    /// <param name="severity">The severity value.</param>
    /// <param name="sheetIndex">The sheetIndex value.</param>
    /// <param name="address">The address value.</param>
    /// <param name="message">The message value.</param>
    internal ReaderDiagnostic(string code, string severity, int? sheetIndex, CellAddress? address, string message)
    {
        Code = code;
        Severity = severity;
        SheetIndex = sheetIndex;
        Address = address;
        Message = message;
    }

    /// <summary>Gets the Code metadata.</summary>
    public string Code { get; }

    /// <summary>Gets the Severity metadata.</summary>
    public string Severity { get; }

    /// <summary>Gets the SheetIndex metadata.</summary>
    public int? SheetIndex { get; }

    /// <summary>Gets the Address metadata.</summary>
    public CellAddress? Address { get; }

    /// <summary>Gets the Message metadata.</summary>
    public string Message { get; }
}

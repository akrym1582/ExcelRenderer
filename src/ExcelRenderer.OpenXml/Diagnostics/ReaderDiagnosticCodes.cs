namespace ExcelRenderer.OpenXml.Diagnostics;

/// <summary>Stable diagnostic codes emitted by the standalone reader.</summary>
public static class ReaderDiagnosticCodes
{
    /// <summary>A formula has no saved result; the reader displays an empty string.</summary>
    public const string FormulaCachedValueMissing = "FormulaCachedValueMissing";

    /// <summary>A saved formula result is an Excel error.</summary>
    public const string FormulaResultError = "FormulaResultError";

    /// <summary>A number format is invalid or unverified; General is used.</summary>
    public const string UnsupportedNumberFormat = "UnsupportedNumberFormat";

    /// <summary>A print definition cannot be resolved in its entirety.</summary>
    public const string UnsupportedPrintReference = "UnsupportedPrintReference";

    /// <summary>A color remains in its source representation without a resolved RGB value.</summary>
    public const string UnresolvedColor = "UnresolvedColor";
}

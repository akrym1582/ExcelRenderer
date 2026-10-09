namespace ExcelRenderer.Core;

/// <summary>A nonfatal conversion diagnostic.</summary>
/// <param name="Code">The diagnostic code.</param>
/// <param name="Message">The diagnostic description.</param>
/// <param name="SheetName">The optional source sheet.</param>
/// <param name="PageNumber">The optional output page.</param>
public sealed record ConversionDiagnostic(string Code, string Message, string? SheetName = null, int? PageNumber = null);

namespace ExcelRenderer.Core;

/// <summary>The completed PDF conversion.</summary>
/// <param name="PageCount">The number of PDF pages.</param>
/// <param name="Diagnostics">Nonfatal conversion diagnostics.</param>
public sealed record ConversionResult(int PageCount, IReadOnlyList<ConversionDiagnostic> Diagnostics);

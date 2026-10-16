namespace ExcelRenderer.Core;

/// <summary>Describes a reader decision made before model construction.</summary>
/// <param name="RequestedBackend">The requested policy.</param>
/// <param name="SelectedBackend">The concrete engine.</param>
/// <param name="ReasonCodes">Stable reasons for the decision.</param>
public sealed record ReaderBackendSelection(ReaderBackend RequestedBackend, ReaderBackend SelectedBackend, IReadOnlyList<string> ReasonCodes);

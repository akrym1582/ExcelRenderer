using System.Collections.Generic;

namespace ExcelRenderer.Mapping;

/// <summary>Describes a template-only engine selection.</summary>
public sealed class MappingBackendSelection
{
    /// <summary>Initializes a new instance of the <see cref="MappingBackendSelection"/> class.</summary>
    /// <param name="requestedBackend">Requested engine policy.</param>
    /// <param name="selectedBackend">Concrete engine selected before data evaluation.</param>
    /// <param name="reasonCodes">Stable reasons for selection.</param>
    internal MappingBackendSelection(MappingBackend requestedBackend, MappingBackend selectedBackend, IReadOnlyList<string> reasonCodes)
    {
        RequestedBackend = requestedBackend;
        SelectedBackend = selectedBackend;
        ReasonCodes = reasonCodes;
    }

    /// <summary>Gets the requested policy.</summary>
    public MappingBackend RequestedBackend { get; }

    /// <summary>Gets the concrete engine.</summary>
    public MappingBackend SelectedBackend { get; }

    /// <summary>Gets stable selection reasons.</summary>
    public IReadOnlyList<string> ReasonCodes { get; }
}

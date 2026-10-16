using System;

namespace ExcelRenderer.Mapping;

/// <summary>Selects the workbook editing engine before evaluating input data.</summary>
public enum MappingBackend
{
    /// <summary>Identifies the removed compatibility writer.</summary>
    [Obsolete("The ClosedXml mapping backend has been removed. Use OpenXml or Auto.")]
    ClosedXml = 0,

    /// <summary>Uses the Open XML writer without fallback. Formula handling is selected separately.</summary>
    OpenXml = 1,

    /// <summary>Uses the Open XML writer after template preflight.</summary>
    Auto = 2,
}

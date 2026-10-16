namespace ExcelRenderer.Mapping;

/// <summary>Selects the workbook editing engine before evaluating input data.</summary>
public enum MappingBackend
{
    /// <summary>Uses the compatibility writer.</summary>
    ClosedXml,

    /// <summary>Uses the formula-free Open XML writer without fallback.</summary>
    OpenXml,

    /// <summary>Checks template capabilities before choosing an engine.</summary>
    Auto,
}

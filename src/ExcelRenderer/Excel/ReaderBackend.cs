namespace ExcelRenderer.Excel;

/// <summary>Selects the workbook reader before model construction.</summary>
public enum ReaderBackend
{
    /// <summary>Retains the removed backend identifier for configuration migration; requests are rejected.</summary>
    [Obsolete("The ClosedXml backend has been removed. Use OpenXml or Auto.")]
    ClosedXml,

    /// <summary>Uses the streaming Open XML reader without fallback.</summary>
    OpenXml,

    /// <summary>Uses OpenXml as a configuration-compatible alias, without fallback.</summary>
    Auto,
}

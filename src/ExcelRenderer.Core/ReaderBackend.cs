namespace ExcelRenderer.Core;

/// <summary>Selects the workbook reader before model construction.</summary>
public enum ReaderBackend
{
    /// <summary>Uses the compatibility reader.</summary>
    ClosedXml,

    /// <summary>Uses the streaming Open XML reader without fallback.</summary>
    OpenXml,

    /// <summary>Inspects capabilities and selects a reader before execution.</summary>
    Auto,
}

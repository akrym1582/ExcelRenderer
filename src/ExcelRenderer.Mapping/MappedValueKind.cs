namespace ExcelRenderer.Mapping;

/// <summary>Scalar categories shared by both mapping executors.</summary>
internal enum MappedValueKind
{
    /// <summary>No stored value.</summary>
    Blank,

    /// <summary>Literal text.</summary>
    String,

    /// <summary>A numeric scalar.</summary>
    Number,

    /// <summary>A boolean value.</summary>
    Boolean,

    /// <summary>A date and time value.</summary>
    DateTime,

    /// <summary>An elapsed time value.</summary>
    TimeSpan,
}

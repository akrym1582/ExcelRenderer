using System;

namespace ExcelRenderer.Mapping.Expansion;

/// <summary>A scalar evaluated once by the common language planner.</summary>
internal readonly struct MappedValue
{
    /// <summary>Initializes a new instance of the <see cref="MappedValue"/> struct.</summary>
    /// <param name="value">A validated CLR scalar or null.</param>
    internal MappedValue(object? value)
    {
        Value = value;
        Kind = value switch
        {
            null => MappedValueKind.Blank,
            string => MappedValueKind.String,
            bool => MappedValueKind.Boolean,
            DateTime => MappedValueKind.DateTime,
            TimeSpan => MappedValueKind.TimeSpan,
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => MappedValueKind.Number,
            _ => throw new ArgumentException("A mapped value must be an Excel scalar.", nameof(value)),
        };
    }

    /// <summary>Gets the explicit scalar category.</summary>
    internal MappedValueKind Kind { get; }

    /// <summary>Gets the original typed scalar.</summary>
    internal object? Value { get; }
}

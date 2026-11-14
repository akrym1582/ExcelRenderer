using System;
using ExcelRenderer.OpenXml.Writing.Plans;

namespace ExcelRenderer.Mapping.Expansion;

/// <summary>A scalar evaluated once by the common language planner.</summary>
internal readonly struct MappedValue
{
    /// <summary>Initializes a new instance of the <see cref="MappedValue"/> struct.</summary>
    /// <param name="value">A validated CLR scalar or null.</param>
    /// <param name="fontColor">Optional RGB font color.</param>
    /// <param name="image">Optional image placement.</param>
    internal MappedValue(object? value, string? fontColor = null, CellImageEdit? image = null)
    {
        Value = value;
        FontColor = fontColor;
        Image = image;
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

    /// <summary>Gets the optional image placement.</summary>
    internal CellImageEdit? Image { get; }

    /// <summary>Gets the optional RGB font color.</summary>
    internal string? FontColor { get; }

    /// <summary>Gets the explicit scalar category.</summary>
    internal MappedValueKind Kind { get; }

    /// <summary>Gets the original typed scalar.</summary>
    internal object? Value { get; }
}

using System.Runtime.CompilerServices;
using ExcelRenderer.Abstractions;
using ExcelRenderer.Model;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Bridges finalized full text geometry while keeping font runs outside Core.</summary>
internal sealed class CoreTextLayoutAdapter : CoreTextMeasurerAdapter, Core.Abstractions.ITextLayoutService
{
    private readonly ITextLayoutService layout;
    private readonly ConditionalWeakTable<TextLayoutResult, Core.Abstractions.TextLayoutResult> layouts = new();

    /// <summary>Initializes a new instance of the <see cref="CoreTextLayoutAdapter"/> class.</summary>
    /// <param name="measurer">The original public measurer.</param>
    /// <param name="layout">Its finalized layout service.</param>
    internal CoreTextLayoutAdapter(ITextMeasurer measurer, ITextLayoutService layout)
        : base(measurer) => this.layout = layout;

    /// <inheritdoc/>
    public Core.Abstractions.TextLayoutResult Layout(string text, Core.Model.FontStyle font, double availableWidth, bool wrap)
    {
        var result = layout.Layout(text, ConvertFont(font), availableWidth, wrap);
        return IsImmutable(result) ? layouts.GetValue(result, ToCore) : ToCore(result);
    }

    /// <summary>Preserves all finalized metrics without measuring again.</summary>
    /// <param name="value">The public layout.</param>
    /// <returns>The neutral layout with borrowed runs.</returns>
    internal static Core.Abstractions.TextLayoutResult ToCore(TextLayoutResult value)
    {
        var immutable = IsImmutable(value);
        var lines = value.Lines.Select(line => new Core.Abstractions.TextLayoutLine(line.Text, line.Width, line.Height, line.Baseline, line.ExplicitBreak)
        {
            Ascent = line.Ascent,
            Descent = line.Descent,
            Leading = line.Leading,
            ExtensionData = new FullLineData(line.Runs is System.Collections.ObjectModel.ReadOnlyCollection<TextLayoutRun> ? line : line with { Runs = Array.AsReadOnly(line.Runs.ToArray()) }),
        }).ToArray();
        var result = new Core.Abstractions.TextLayoutResult(new(value.Size.Width, value.Size.Height), immutable ? Array.AsReadOnly(lines) : lines)
        {
            ExtensionData = immutable ? new FullTextLayoutData(value) : null,
        };
        return value.HasExplicitEffectiveFontSize ? result with { EffectiveFontSize = value.EffectiveFontSize } : result;
    }

    /// <summary>Materializes one page's finalized geometry and scaled borrowed runs.</summary>
    /// <param name="value">The neutral finalized layout.</param>
    /// <returns>The public finalized layout.</returns>
    internal static TextLayoutResult ToPublic(Core.Abstractions.TextLayoutResult value)
    {
        if (value.ExtensionData is FullTextLayoutData original && Unchanged(value, original.Layout))
        {
            return original.Layout;
        }

        var result = new TextLayoutResult(new(value.Size.Width, value.Size.Height), value.Lines.Select(ConvertLine).ToArray());
        return value.HasExplicitEffectiveFontSize ? result with { EffectiveFontSize = value.EffectiveFontSize } : result;
    }

    private static bool IsImmutable(TextLayoutResult value)
    {
        if (value.Lines is not System.Collections.ObjectModel.ReadOnlyCollection<TextLayoutLine>)
        {
            return false;
        }

        for (var index = 0; index < value.Lines.Count; index++)
        {
            if (value.Lines[index].Runs is not System.Collections.ObjectModel.ReadOnlyCollection<TextLayoutRun>)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Unchanged(Core.Abstractions.TextLayoutResult value, TextLayoutResult original)
    {
        if (value.Size.Width != original.Size.Width || value.Size.Height != original.Size.Height || value.HasExplicitEffectiveFontSize != original.HasExplicitEffectiveFontSize || value.EffectiveFontSize != original.EffectiveFontSize || value.Lines.Count != original.Lines.Count)
        {
            return false;
        }

        for (var index = 0; index < value.Lines.Count; index++)
        {
            var line = value.Lines[index];
            if (line.ExtensionData is not FullLineData data || !ReferenceEquals(data.Line, original.Lines[index]) || line.ExtensionScale != 1 || line.Text != data.Line.Text || line.Width != data.Line.Width || line.Height != data.Line.Height || line.Baseline != data.Line.Baseline || line.Ascent != data.Line.Ascent || line.Descent != data.Line.Descent || line.Leading != data.Line.Leading || line.ExplicitBreak != data.Line.ExplicitBreak)
            {
                return false;
            }
        }

        return true;
    }

    private static TextLayoutLine ConvertLine(Core.Abstractions.TextLayoutLine line)
    {
        var runs = line.ExtensionData is FullLineData data
            ? line.ExtensionScale == 1 ? data.Runs : data.Runs.Select(run => run with { X = run.X * line.ExtensionScale, Advance = run.Advance * line.ExtensionScale }).ToArray()
            : [];
        return new(line.Text, line.Width, line.Height, line.Baseline, runs, line.ExplicitBreak)
        {
            Ascent = line.Ascent,
            Descent = line.Descent,
            Leading = line.Leading,
        };
    }
}

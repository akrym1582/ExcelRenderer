using System.Globalization;
using ExcelRenderer.Core.Model;
using ExcelRenderer.Formulas.Evaluation;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;
using ExcelRenderer.OpenXml.Calculation;
using ExcelRenderer.OpenXml.Reader.Model.Workbook;
using static ExcelRenderer.Core.Excel.OpenXmlStyleConverter;

namespace ExcelRenderer.CoreIntegration;

/// <summary>Prepares standard two/three-point scales and returns a typed solid background.</summary>
internal sealed class ConditionalColorScale
{
    private readonly ReportColor[] colors;
    private readonly ConditionalFormattingThresholdResolver resolver;

    /// <summary>Initializes a new instance of the <see cref="ConditionalColorScale"/> class.</summary>
    /// <param name="rule">Resolved source rule.</param>
    /// <param name="options">Formula parser limits.</param>
    internal ConditionalColorScale(ElementInfo rule, FormulaEvaluationOptions? options)
    {
        var scale = Child(rule, "colorScale");
        var points = scale?.Children.Where(child => child.Name == "cfvo").ToArray() ?? [];
        var colorSources = scale?.Children.Where(child => child.Name == "color").ToArray() ?? [];
        if (points.Length is not (2 or 3) || colorSources.Length != points.Length || scale!.Children.Count != points.Length * 2)
        {
            throw Invalid("A standard scale requires two or three corresponding cfvo/color pairs.");
        }

        colors = colorSources.Select(color => ConditionalStyle.ResolvedColor(color) ?? throw Invalid("Unresolved scale color.")).ToArray();
        resolver = new(points, options);
    }

    /// <summary>Gets a value indicating whether percentile thresholds need sorted values.</summary>
    internal bool NeedsSort => resolver.NeedsSort;

    private IReadOnlyList<double> Thresholds => resolver.Values;

    /// <summary>Resolves scale thresholds while preserving the existing collapsed-interval policy.</summary>
    /// <param name="population">The population input.</param>
    /// <param name="calculation">The calculation input.</param>
    /// <param name="sheet">The sheet input.</param>
    /// <param name="anchor">The anchor input.</param>
    /// <param name="token">The token input.</param>
    internal void Initialize(ConditionalPopulation population, WorkbookCalculationSession calculation, string sheet, OpenXml.Reader.Model.Cells.CellAddress anchor, CancellationToken token)
    {
        resolver.Initialize(population, calculation, sheet, anchor, token);
        if (population.Numbers.Count > 0 && Thresholds.Zip(Thresholds.Skip(1), (first, last) => first == last).Any(equal => equal))
        {
            throw Invalid("Collapsed scale interval; ordinary background is retained.");
        }
    }

    /// <summary>Interpolates sRGB bytes with one final round away from zero.</summary>
    /// <param name="number">Numeric source cell value.</param>
    /// <returns>The clamped solid background color.</returns>
    internal ReportColor Color(double number)
    {
        if (number <= Thresholds[0])
        {
            return colors[0];
        }

        if (number >= Thresholds[Thresholds.Count - 1])
        {
            return colors[colors.Length - 1];
        }

        var segment = Thresholds.Count == 3 && number > Thresholds[1] ? 1 : 0;
        var start = Thresholds[segment];
        var end = Thresholds[segment + 1];
        var ratio = ConditionalPopulation.Finite(end - start) ? (number - start) / (end - start) : ((number / 2) - (start / 2)) / ((end / 2) - (start / 2));
        var first = colors[segment];
        var last = colors[segment + 1];
        return new(Component(first.Red, last.Red), Component(first.Green, last.Green), Component(first.Blue, last.Blue));

        byte Component(byte low, byte high) => (byte)Math.Round(Interpolate(low, high, ratio), MidpointRounding.AwayFromZero);
    }

    private static double Interpolate(double first, double last, double ratio) => ((1 - ratio) * first) + (ratio * last);

    private static ConditionalFormattingException Invalid(string message) => new("ConditionalFormattingThresholdInvalid", message);
}

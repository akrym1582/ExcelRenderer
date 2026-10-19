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
    private readonly ElementInfo[] points;
    private readonly ReportColor[] colors;
    private readonly ParsedFormula?[] formulas;
    private readonly double[] thresholds;

    /// <summary>Initializes a new instance of the <see cref="ConditionalColorScale"/> class.</summary>
    /// <param name="rule">Resolved source rule.</param>
    /// <param name="options">Formula parser limits.</param>
    internal ConditionalColorScale(ElementInfo rule, FormulaEvaluationOptions? options)
    {
        var scale = Child(rule, "colorScale");
        points = scale?.Children.Where(child => child.Name == "cfvo").ToArray() ?? [];
        var colorSources = scale?.Children.Where(child => child.Name == "color").ToArray() ?? [];
        if (points.Length is not (2 or 3) || colorSources.Length != points.Length || scale!.Children.Count != points.Length * 2)
        {
            throw Invalid("A standard scale requires two or three corresponding cfvo/color pairs.");
        }

        colors = colorSources.Select(color => ConditionalStyle.ResolvedColor(color) ?? throw Invalid("Unresolved scale color.")).ToArray();
        thresholds = new double[points.Length];
        formulas = new ParsedFormula?[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            var type = Attribute(point, "type");
            if (type is not ("min" or "max" or "num" or "percent" or "percentile" or "formula"))
            {
                throw Invalid("Unsupported threshold type.");
            }

            if (type == "formula")
            {
                var parsed = FormulaParser.Parse(Attribute(point, "val") ?? Child(point, "formula")?.Text ?? string.Empty, options);
                if (!parsed.Success)
                {
                    throw new ConditionalFormattingException(parsed.Diagnostics.Any(diagnostic => diagnostic.Code == "ResourceLimitExceeded") ? "ConditionalFormattingLimitExceeded" : "ConditionalFormattingThresholdInvalid", "Invalid threshold formula.");
                }

                formulas[index] = parsed.Formula;
            }
            else if (type is "num" or "percent" or "percentile")
            {
                thresholds[index] = Number(Attribute(point, "val"));
                if (type != "num" && (thresholds[index] < 0 || thresholds[index] > 100))
                {
                    throw Invalid("Threshold percent/percentile must be between zero and 100.");
                }
            }
        }
    }

    /// <summary>Gets a value indicating whether a percentile requires sorted numeric values.</summary>
    internal bool NeedsSort => points.Any(point => Attribute(point, "type") == "percentile");

    /// <summary>Resolves all thresholds once at the saved rule anchor.</summary>
    /// <param name="population">Prepared shared numeric population.</param>
    /// <param name="calculation">Shared value policy and dependency cache.</param>
    /// <param name="sheet">Source sheet.</param>
    /// <param name="anchor">Saved first sqref anchor.</param>
    /// <param name="token">Cooperative cancellation.</param>
    internal void Initialize(ConditionalPopulation population, WorkbookCalculationSession calculation, string sheet, OpenXml.Reader.Model.Cells.CellAddress anchor, CancellationToken token)
    {
        if (population.Numbers.Count == 0)
        {
            return;
        }

        var min = population.Minimum;
        var max = population.Maximum;
        for (var index = 0; index < points.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var type = Attribute(points[index], "type");
            thresholds[index] = type switch
            {
                "min" => min,
                "max" => max,
                "percent" => Interpolate(min, max, thresholds[index] / 100),
                "percentile" => Percentile(population.Numbers, thresholds[index] / 100),
                "formula" => Formula(index),
                _ => thresholds[index],
            };
            if (!ConditionalPopulation.Finite(thresholds[index]) || (index > 0 && thresholds[index] < thresholds[index - 1]))
            {
                throw Invalid("Nonfinite or descending scale thresholds.");
            }
        }

        // Without an independent Excel oracle, do not invent a winner for collapsed intervals.
        if (thresholds.Zip(thresholds.Skip(1), (first, last) => first == last).Any(equal => equal))
        {
            throw Invalid("Collapsed scale interval; ordinary background is retained.");
        }

        double Formula(int index)
        {
            var origin = new FormulaCellAddress(sheet, anchor.Row, anchor.Column);
            var result = calculation.EvaluateFormula(formulas[index]!, origin, origin);
            if (!result.Success)
            {
                throw new FormulaCalculationException(origin, result.Diagnostics);
            }

            return result.Value.Kind == FormulaValueKind.Number ? result.Value.Number : throw Invalid("Threshold formula did not return a number.");
        }
    }

    /// <summary>Interpolates sRGB bytes with one final round away from zero.</summary>
    /// <param name="number">Numeric source cell value.</param>
    /// <returns>The clamped solid background color.</returns>
    internal ReportColor Color(double number)
    {
        if (number <= thresholds[0])
        {
            return colors[0];
        }

        if (number >= thresholds[thresholds.Length - 1])
        {
            return colors[colors.Length - 1];
        }

        var segment = thresholds.Length == 3 && number > thresholds[1] ? 1 : 0;
        var start = thresholds[segment];
        var end = thresholds[segment + 1];
        var ratio = ConditionalPopulation.Finite(end - start) ? (number - start) / (end - start) : ((number / 2) - (start / 2)) / ((end / 2) - (start / 2));
        var first = colors[segment];
        var last = colors[segment + 1];
        return new(Component(first.Red, last.Red), Component(first.Green, last.Green), Component(first.Blue, last.Blue));

        byte Component(byte low, byte high) => (byte)Math.Round(Interpolate(low, high, ratio), MidpointRounding.AwayFromZero);
    }

    private static double Percentile(IReadOnlyList<double> numbers, double percent)
    {
        var position = (numbers.Count - 1) * percent;
        var low = (int)Math.Floor(position);
        var high = (int)Math.Ceiling(position);
        return Interpolate(numbers[low], numbers[high], position - low);
    }

    private static double Interpolate(double first, double last, double ratio) => ((1 - ratio) * first) + (ratio * last);

    private static double Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && ConditionalPopulation.Finite(number) ? number : throw Invalid("Invalid numeric threshold.");

    private static ConditionalFormattingException Invalid(string message) => new("ConditionalFormattingThresholdInvalid", message);
}

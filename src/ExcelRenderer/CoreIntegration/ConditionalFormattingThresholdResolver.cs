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

/// <summary>Resolves saved visual thresholds once against the shared population.</summary>
internal sealed class ConditionalFormattingThresholdResolver
{
    private readonly ElementInfo[] points;
    private readonly ParsedFormula?[] formulas;
    private readonly double[] thresholds;
    private readonly bool extension;
    private readonly bool strictArithmetic;

    /// <summary>Initializes a new instance of the <see cref="ConditionalFormattingThresholdResolver"/> class.</summary>
    /// <param name="points">The points input.</param>
    /// <param name="options">The options input.</param>
    /// <param name="extension">The extension input.</param>
    /// <param name="strictArithmetic">Whether finite differences are required by the visual profile.</param>
    /// <param name="icons">The icons input.</param>
    internal ConditionalFormattingThresholdResolver(ElementInfo[] points, FormulaEvaluationOptions? options, bool extension = false, bool icons = false, bool strictArithmetic = false)
    {
        this.points = points;
        this.extension = extension;
        this.strictArithmetic = strictArithmetic;
        thresholds = new double[points.Length];
        formulas = new ParsedFormula?[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            var type = Attribute(point, "type");
            if (icons ? type is not ("num" or "percent" or "percentile" or "formula") : type is not ("min" or "max" or "autoMin" or "autoMax" or "num" or "percent" or "percentile" or "formula") || (!extension && type is "autoMin" or "autoMax"))
            {
                throw Invalid("Unsupported threshold type.");
            }

            if (type == "formula")
            {
                var text = extension ? point.Children.FirstOrDefault(child => child.Name == "f" && child.NamespaceUri == ConditionalFormattingRuleNormalizer.Xm)?.Text : Attribute(point, "val") ?? Child(point, "formula")?.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw Invalid("Missing formula or relative x14 threshold reference.");
                }

                var parsed = FormulaParser.Parse(text, options);
                if (!parsed.Success)
                {
                    throw new ConditionalFormattingException(parsed.Diagnostics.Any(diagnostic => diagnostic.Code == "ResourceLimitExceeded") ? "ConditionalFormattingLimitExceeded" : "ConditionalFormattingThresholdInvalid", "Invalid threshold formula.");
                }

                if (extension && HasRelativeReference(parsed.Formula!.Root))
                {
                    throw Invalid("Relative x14 threshold reference.");
                }

                formulas[index] = parsed.Formula;
            }
            else if (type is "num" or "percent" or "percentile")
            {
                thresholds[index] = Number(extension ? point.Children.FirstOrDefault(child => child.Name == "f" && child.NamespaceUri == ConditionalFormattingRuleNormalizer.Xm)?.Text ?? "0" : Attribute(point, "val"));
                if (type != "num" && (thresholds[index] < 0 || thresholds[index] > 100))
                {
                    throw Invalid("Threshold percent/percentile must be between zero and 100.");
                }
            }
        }
    }

    /// <summary>Gets thresholds resolved once at the saved anchor.</summary>
    internal IReadOnlyList<double> Values => thresholds;

    /// <summary>Gets a value indicating whether percentile thresholds need sorted values.</summary>
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
                "autoMin" => Math.Min(0, min),
                "autoMax" => Math.Max(0, max),
                "percent" => !strictArithmetic ? Interpolate(min, max, thresholds[index] / 100) : ConditionalPopulation.Finite(max - min) ? min + ((max - min) * thresholds[index] / 100) : throw Invalid("Nonfinite threshold difference."),
                "percentile" => Percentile(population.Numbers, thresholds[index] / 100),
                "formula" => Formula(index),
                _ => thresholds[index],
            };
            if (!ConditionalPopulation.Finite(thresholds[index]) || (index > 0 && thresholds[index] < thresholds[index - 1]))
            {
                throw Invalid("Nonfinite or descending scale thresholds.");
            }
        }

        double Formula(int index)
        {
            if (extension && IsNumericLiteral(formulas[index]!.Root))
            {
                return 0;
            }

            var origin = new FormulaCellAddress(sheet, anchor.Row, anchor.Column);
            var result = calculation.EvaluateFormula(formulas[index]!, origin, origin);
            if (!result.Success)
            {
                throw new FormulaCalculationException(origin, result.Diagnostics);
            }

            return result.Value.Kind == FormulaValueKind.Number ? result.Value.Number : throw Invalid("Threshold formula did not return a number.");
        }
    }

    private static bool IsNumericLiteral(FormulaNode node) => node.Kind == FormulaNodeKind.Number ||
        ((node.Kind is FormulaNodeKind.Unary or FormulaNodeKind.Parenthesized) && node.Children.Count == 1 && IsNumericLiteral(node.Children[0]));

    private static bool HasRelativeReference(FormulaNode node) =>
        (node.Kind == FormulaNodeKind.Cell && ((!node.AbsoluteRow && node.Row != 0) || (!node.AbsoluteColumn && node.Column != 0))) || node.Children.Any(HasRelativeReference);

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

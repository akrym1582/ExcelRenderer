using System;
using System.Globalization;
using ExcelRenderer.Formulas.Values;

namespace ExcelRenderer.Formulas.Criteria
{
    // These coercions form an explicit profile until the case matrix is observed in Excel.

    /// <summary>Compiles one conditional aggregate criterion using the explicit coercion profile.</summary>
    internal sealed class CriteriaMatcher
    {
        private readonly string op = "=";
        private readonly FormulaValue operand;
        private readonly WildcardMatcher? wildcard;

        /// <summary>Initializes a new instance of the <see cref="CriteriaMatcher"/> class.</summary>
        /// <param name="criteria">Scalar criterion, with blank references interpreted as zero.</param>
        internal CriteriaMatcher(FormulaValue criteria)
        {
            if (criteria.Kind == FormulaValueKind.Blank)
            {
                criteria = FormulaValue.FromNumber(0);
            }

            if (criteria.Kind == FormulaValueKind.Text)
            {
                var text = criteria.Text;
                var length = text.StartsWith("<=", StringComparison.Ordinal) || text.StartsWith(">=", StringComparison.Ordinal) || text.StartsWith("<>", StringComparison.Ordinal) ? 2 : text.Length > 0 && (text[0] == '=' || text[0] == '<' || text[0] == '>') ? 1 : 0;
                if (length > 0)
                {
                    op = text.Substring(0, length);
                    text = text.Substring(length);
                }

                if (TryNumber(text, out var number))
                {
                    operand = FormulaValue.FromNumber(number);
                }
                else
                {
                    operand = FormulaValue.FromText(text);
                    if ((op == "=" || op == "<>") && WildcardMatcher.HasPattern(text))
                    {
                        wildcard = new WildcardMatcher(text);
                    }
                }
            }
            else
            {
                operand = criteria;
            }
        }

        /// <summary>Tests one candidate without reparsing the criterion.</summary>
        /// <param name="value">Candidate value.</param>
        /// <param name="tick">Session work budget callback.</param>
        /// <returns>Whether the criterion matches.</returns>
        internal bool Matches(FormulaValue value, Action tick)
        {
            tick();
            if (operand.Kind == FormulaValueKind.Number)
            {
                double number;
                if (value.Kind == FormulaValueKind.Blank)
                {
                    number = 0;
                }
                else if (value.Kind == FormulaValueKind.Number)
                {
                    number = value.Number;
                }
                else if (value.Kind == FormulaValueKind.Text && TryNumber(value.Text, out number))
                {
                }
                else
                {
                    return op == "<>" && value.Kind != FormulaValueKind.Error;
                }

                return Compare(number.CompareTo(operand.Number));
            }

            if (operand.Kind == FormulaValueKind.Boolean)
            {
                return value.Kind == FormulaValueKind.Boolean ? Compare(value.Boolean.CompareTo(operand.Boolean)) : op == "<>" && value.Kind != FormulaValueKind.Error;
            }

            if (value.Kind == FormulaValueKind.Error)
            {
                return (op == "=" || op == "<>") && Compare(StringComparer.OrdinalIgnoreCase.Compare(value.Error, operand.Text));
            }

            bool textValue = value.Kind == FormulaValueKind.Text || value.Kind == FormulaValueKind.Blank;
            if (!textValue)
            {
                return op == "<>";
            }

            if (wildcard != null)
            {
                var equal = wildcard.Matches(value.Text, tick);

                // Wildcards match text; an empty operand also matches missing cells.
                equal &= value.Kind != FormulaValueKind.Blank || operand.Text.Length == 0;
                return op == "<>" ? !equal : equal;
            }

            return Compare(StringComparer.OrdinalIgnoreCase.Compare(value.Text, operand.Text));
        }

        private static bool TryNumber(string text, out double number) => double.TryParse(text.Trim(' ', '\t', '\r', '\n'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number);

        private bool Compare(int comparison) => op == "=" ? comparison == 0 : op == "<>" ? comparison != 0 : op == "<" ? comparison < 0 : op == "<=" ? comparison <= 0 : op == ">" ? comparison > 0 : comparison >= 0;
    }
}

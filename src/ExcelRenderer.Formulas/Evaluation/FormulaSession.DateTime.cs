using System;
using ExcelRenderer.Formulas.Functions.Dates;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;

namespace ExcelRenderer.Formulas.Evaluation
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private FormulaValue DateFunction(string name, FormulaNode node, FormulaCellAddress origin)
        {
            var numbers = new double[node.Children.Count];
            for (var i = 0; i < numbers.Length; i++)
            {
                var value = Numeric(Scalar(Eval(node.Children[i], origin), origin));
                if (value.Kind == FormulaValueKind.Error)
                {
                    return value;
                }

                numbers[i] = value.Number;
            }

            var system = options.DateSystem;
            if (name == "DATE")
            {
                var year = Math.Truncate(numbers[0]);
                var month = Math.Truncate(numbers[1]);
                var day = Math.Truncate(numbers[2]);
                if (year < 0 || year > 9999 || Math.Abs(month) > 120000 || Math.Abs(day) > 4000000)
                {
                    return FormulaValue.FromError("#NUM!");
                }

                year += year < 1900 ? 1900 : 0;
                var totalMonths = (year * 12) + month - 1;
                var normalizedYear = Math.Floor(totalMonths / 12);
                var normalizedMonth = totalMonths - (normalizedYear * 12) + 1;
                if (normalizedYear < 1 || normalizedYear > 9999)
                {
                    return FormulaValue.FromError("#NUM!");
                }

                var serial = ExcelDate.Serial((int)normalizedYear, (int)normalizedMonth, 1, system) + day - 1;
                return serial < 0 || serial > ExcelDate.Maximum(system) ? FormulaValue.FromError("#NUM!") : Number(serial);
            }

            if (!ExcelDate.TryRead(numbers[0], system, out var date))
            {
                return FormulaValue.FromError("#NUM!");
            }

            if (name == "YEAR" || name == "MONTH" || name == "DAY")
            {
                return Number(name == "YEAR" ? date.Year : name == "MONTH" ? date.Month : date.Day);
            }

            var months = Math.Truncate(numbers[1]);
            if (Math.Abs(months) > 120000)
            {
                return FormulaValue.FromError("#NUM!");
            }

            var targetMonths = (date.Year * 12d) + date.Month - 1 + months;
            var targetYear = Math.Floor(targetMonths / 12);
            var targetMonth = targetMonths - (targetYear * 12) + 1;
            if (targetYear < 1 || targetYear > 9999)
            {
                return FormulaValue.FromError("#NUM!");
            }

            var lastDay = ExcelDate.DaysInMonth((int)targetYear, (int)targetMonth, system);
            var targetDay = name == "EOMONTH" ? lastDay : Math.Min(date.Day, lastDay);
            var result = ExcelDate.Serial((int)targetYear, (int)targetMonth, targetDay, system);
            return result < 0 || result > ExcelDate.Maximum(system) ? FormulaValue.FromError("#NUM!") : Number(result);
        }
    }
}

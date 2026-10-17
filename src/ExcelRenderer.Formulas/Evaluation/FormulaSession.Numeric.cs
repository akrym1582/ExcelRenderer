using System;
using System.Collections.Generic;
using ExcelRenderer.Formulas.Functions.Numeric;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;

namespace ExcelRenderer.Formulas.Evaluation
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private Item NumericFunction(string name, IReadOnlyList<FormulaNode> args, FormulaCellAddress origin)
        {
            var value = Scalar(Eval(args[0], origin), origin);
            value = Numeric(value);
            if (value.Kind == FormulaValueKind.Error)
            {
                return new Item(value);
            }

            if (name == "ABS")
            {
                return new Item(Number(Math.Abs(value.Number)));
            }

            if (name == "INT")
            {
                return new Item(Number(Math.Floor(value.Number)));
            }

            var second = Numeric(Scalar(Eval(args[1], origin), origin));
            if (second.Kind == FormulaValueKind.Error)
            {
                return new Item(second);
            }

            if (name == "MOD")
            {
                return new Item(second.Number == 0 ? FormulaValue.FromError("#DIV/0!") : Number(value.Number - (second.Number * Math.Floor(value.Number / second.Number))));
            }

            return new Item(Number(DecimalRounding.Round(value.Number, second.Number, name)));
        }
    }
}

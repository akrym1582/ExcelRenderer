using System;
using System.Collections.Generic;
using ExcelRenderer.Formulas.References;
using ExcelRenderer.Formulas.Syntax;
using ExcelRenderer.Formulas.Values;

namespace ExcelRenderer.Formulas.Evaluation
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private Item LogicalFunction(string name, IReadOnlyList<FormulaNode> args, FormulaCellAddress origin)
        {
            if (name == "TRUE" || name == "FALSE")
            {
                return new Item(FormulaValue.FromBoolean(name == "TRUE"));
            }

            if (name == "IF")
            {
                var condition = Logical(Scalar(Eval(args[0], origin), origin));
                if (condition.Kind == FormulaValueKind.Error)
                {
                    return new Item(condition);
                }

                return condition.Boolean ? Eval(args[1], origin) : args.Count == 3 ? Eval(args[2], origin) : new Item(FormulaValue.FromBoolean(false));
            }

            if (name == "IFERROR")
            {
                var first = Eval(args[0], origin);
                if (first.IsReference && (first.Range.FirstRow != first.Range.LastRow || first.Range.FirstColumn != first.Range.LastColumn))
                {
                    Fail("UnsupportedArrayEvaluation", "IFERROR requires a scalar first argument.", origin, args[0].Span, "IFERROR");
                }

                return Scalar(first, origin).Kind == FormulaValueKind.Error ? Eval(args[1], origin) : first;
            }

            var value = Logical(Scalar(Eval(args[0], origin), origin));
            return new Item(value.Kind == FormulaValueKind.Error ? value : FormulaValue.FromBoolean(!value.Boolean));
        }
    }
}

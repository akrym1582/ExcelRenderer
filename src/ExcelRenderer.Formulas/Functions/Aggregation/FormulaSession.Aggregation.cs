using System;
using System.Collections.Generic;

namespace ExcelRenderer.Formulas
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private FormulaValue Aggregate(string name, IReadOnlyList<FormulaNode> args, FormulaCellAddress origin)
        {
            double sum = 0;
            double extreme = 0;
            int count = 0;
            bool logical = name == "AND";
            FormulaValue? error = null;
            foreach (var arg in args)
            {
                var item = Eval(arg, origin);
                foreach (var raw in Enumerate(item, origin))
                {
                    Tick(origin, arg.Span);
                    if (name == "COUNTA")
                    {
                        if (raw.Kind != FormulaValueKind.Blank)
                        {
                            count++;
                        }

                        continue;
                    }

                    if (name == "COUNT")
                    {
                        if (raw.Kind == FormulaValueKind.Number || (!item.IsReference && Numeric(raw).Kind == FormulaValueKind.Number && raw.Kind != FormulaValueKind.Blank))
                        {
                            count++;
                        }

                        continue;
                    }

                    if (raw.Kind == FormulaValueKind.Error)
                    {
                        error ??= raw;
                        continue;
                    }

                    if (name == "AND" || name == "OR")
                    {
                        if (item.IsReference && (raw.Kind == FormulaValueKind.Text || raw.Kind == FormulaValueKind.Blank))
                        {
                            continue;
                        }

                        var boolean = Logical(raw);
                        if (boolean.Kind == FormulaValueKind.Error)
                        {
                            error ??= boolean;
                            continue;
                        }

                        logical = name == "AND" ? logical && boolean.Boolean : logical || boolean.Boolean;
                        count++;
                        continue;
                    }

                    if (item.IsReference && raw.Kind != FormulaValueKind.Number)
                    {
                        continue;
                    }

                    var value = Numeric(raw);
                    if (value.Kind == FormulaValueKind.Error)
                    {
                        error ??= value;
                        continue;
                    }

                    sum += value.Number;
                    if (count == 0 || (name == "MIN" && value.Number < extreme) || (name == "MAX" && value.Number > extreme))
                    {
                        extreme = value.Number;
                    }

                    count++;
                }
            }

            if (error.HasValue)
            {
                return error.Value;
            }

            if (name == "COUNT" || name == "COUNTA")
            {
                return Number(count);
            }

            if (name == "AND" || name == "OR")
            {
                return count == 0 ? FormulaValue.FromError("#VALUE!") : FormulaValue.FromBoolean(logical);
            }

            if (name == "AVERAGE")
            {
                return count == 0 ? FormulaValue.FromError("#DIV/0!") : Number(sum / count);
            }

            return Number(name == "MIN" || name == "MAX" ? extreme : sum);
        }
    }
}

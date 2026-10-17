using System;
using System.Collections.Generic;
using System.Text;

namespace ExcelRenderer.Formulas
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private FormulaValue TextFunction(string name, FormulaNode node, FormulaCellAddress origin)
        {
            var args = new List<FormulaValue>();
            foreach (var child in node.Children)
            {
                var value = Scalar(Eval(child, origin), origin);
                if (value.Kind == FormulaValueKind.Error)
                {
                    return value;
                }

                args.Add(value);
            }

            if (name == "CONCATENATE")
            {
                long length = 0;
                var parts = new string[args.Count];
                for (var i = 0; i < args.Count; i++)
                {
                    parts[i] = args[i].ToString();
                    length += parts[i].Length;
                    CheckStringLength(length, origin, node.Span);
                }

                return FormulaValue.FromText(string.Concat(parts));
            }

            var text = args[0].ToString();
            if (name == "LEN")
            {
                return Number(text.Length);
            }

            if (name == "TRIM")
            {
                var builder = new StringBuilder(text.Length);
                bool pendingSpace = false;
                foreach (var c in text)
                {
                    Tick(origin, node.Span);
                    if (c == ' ')
                    {
                        pendingSpace = builder.Length > 0;
                    }
                    else
                    {
                        if (pendingSpace)
                        {
                            builder.Append(' ');
                            pendingSpace = false;
                        }

                        builder.Append(c);
                    }
                }

                return TextResult(builder.ToString(), origin, node.Span);
            }

            if (name == "SUBSTITUTE")
            {
                return Substitute(text, args, origin, node.Span);
            }

            if (name == "TEXT")
            {
                var number = Numeric(args[0]);
                if (number.Kind == FormulaValueKind.Error)
                {
                    return number;
                }

                return FormatText(number.Number, args[1].ToString(), origin, node.Span);
            }

            var count = args.Count == 1 ? Number(1) : Numeric(args[name == "MID" ? 2 : 1]);
            if (count.Kind == FormulaValueKind.Error)
            {
                return count;
            }

            if (count.Number < 0)
            {
                return FormulaValue.FromError("#VALUE!");
            }

            double lengthValue = Math.Truncate(count.Number);
            if (name == "LEFT" || name == "RIGHT")
            {
                var length = (int)Math.Min(text.Length, lengthValue);
                return TextResult(text.Substring(name == "LEFT" ? 0 : text.Length - length, length), origin, node.Span);
            }

            var start = Numeric(args[1]);
            if (start.Kind == FormulaValueKind.Error)
            {
                return start;
            }

            var position = Math.Truncate(start.Number);
            if (position < 1)
            {
                return FormulaValue.FromError("#VALUE!");
            }

            if (position > text.Length)
            {
                return FormulaValue.FromText(string.Empty);
            }

            int offset = (int)position - 1;
            return TextResult(text.Substring(offset, (int)Math.Min(text.Length - offset, lengthValue)), origin, node.Span);
        }

        private FormulaValue Substitute(string text, IReadOnlyList<FormulaValue> args, FormulaCellAddress origin, SourceSpan span)
        {
            var oldText = args[1].ToString();
            var newText = args[2].ToString();
            double occurrence = 0;
            if (args.Count == 4)
            {
                var value = Numeric(args[3]);
                if (value.Kind == FormulaValueKind.Error)
                {
                    return value;
                }

                occurrence = Math.Truncate(value.Number);
                if (occurrence < 1)
                {
                    return FormulaValue.FromError("#VALUE!");
                }
            }

            if (oldText.Length == 0)
            {
                return TextResult(text, origin, span);
            }

            var matches = new List<int>();
            int offset = 0;
            int found;
            long length = text.Length;
            int count = 0;
            while (offset <= text.Length - oldText.Length && (found = text.IndexOf(oldText, offset, StringComparison.Ordinal)) >= 0)
            {
                Tick(origin, span);
                count++;
                if (occurrence == 0 || count == occurrence)
                {
                    matches.Add(found);
                    length += (long)newText.Length - oldText.Length;
                }

                offset = found + oldText.Length;
                if (occurrence != 0 && count >= occurrence)
                {
                    break;
                }
            }

            CheckStringLength(length, origin, span);
            var builder = new StringBuilder((int)length);
            offset = 0;
            foreach (var match in matches)
            {
                Tick(origin, span);
                builder.Append(text, offset, match - offset);
                builder.Append(newText);
                offset = match + oldText.Length;
            }

            builder.Append(text, offset, text.Length - offset);
            return FormulaValue.FromText(builder.ToString());
        }

        private FormulaValue TextResult(string text, FormulaCellAddress origin, SourceSpan span)
        {
            CheckStringLength(text.Length, origin, span);
            return FormulaValue.FromText(text);
        }
    }
}

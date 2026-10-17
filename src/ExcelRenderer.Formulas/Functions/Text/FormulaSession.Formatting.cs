using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ExcelRenderer.Formulas
{
    /// <summary>Implements bounded session evaluation for this function family.</summary>
    public sealed partial class FormulaSession
    {
        private FormulaValue FormatText(double value, string format, FormulaCellAddress origin, SourceSpan span)
        {
            var parts = new List<(char Kind, string Text)>();
            bool date = false;
            var pattern = new StringBuilder();
            for (var i = 0; i < format.Length; i++)
            {
                Tick(origin, span);
                var c = char.ToLowerInvariant(format[i]);
                if (c == '"')
                {
                    var end = format.IndexOf('"', i + 1);
                    if (end < 0)
                    {
                        return UnsupportedFormat(origin, span);
                    }

                    parts.Add(('l', format.Substring(i + 1, end - i - 1)));
                    pattern.Append('L');
                    i = end;
                }
                else if (c == 'y' || c == 'm' || c == 'd')
                {
                    int start = i;
                    while (i + 1 < format.Length && char.ToLowerInvariant(format[i + 1]) == c)
                    {
                        i++;
                    }

                    int count = i - start + 1;
                    if (c == 'y' ? count != 4 : count > 2)
                    {
                        return UnsupportedFormat(origin, span);
                    }

                    date = true;
                    parts.Add((c, new string(c, count)));
                    pattern.Append(c);
                }
                else if (c == '0' || c == '#' || c == ',' || c == '.' || c == '%')
                {
                    parts.Add((c, c.ToString()));
                    pattern.Append(c);
                }
                else if (c == '-' || c == '/' || c == ' ' || c == '年' || c == '月' || c == '日')
                {
                    parts.Add(('l', c.ToString()));
                    pattern.Append('L');
                }
                else
                {
                    return UnsupportedFormat(origin, span);
                }
            }

            if (date)
            {
                string order = string.Empty;
                long length = 0;
                foreach (var part in parts)
                {
                    if (part.Kind != 'l' && part.Kind != 'y' && part.Kind != 'm' && part.Kind != 'd')
                    {
                        return UnsupportedFormat(origin, span);
                    }

                    if (part.Kind != 'l')
                    {
                        order += part.Kind;
                    }

                    length += part.Kind == 'l' ? part.Text.Length : part.Kind == 'y' ? 4 : part.Text.Length;
                }

                if (order != "ymd" && order != "ym")
                {
                    return UnsupportedFormat(origin, span);
                }

                if (!ExcelDate.TryRead(value, options.DateSystem, out var calendar))
                {
                    return FormulaValue.FromError("#NUM!");
                }

                foreach (var part in parts)
                {
                    if (part.Text.Length == 1 && ((part.Kind == 'm' && calendar.Month >= 10) || (part.Kind == 'd' && calendar.Day >= 10)))
                    {
                        length++;
                    }
                }

                CheckStringLength(length, origin, span);
                var output = new StringBuilder((int)length);
                foreach (var part in parts)
                {
                    Tick(origin, span);
                    if (part.Kind == 'l')
                    {
                        output.Append(part.Text);
                    }
                    else
                    {
                        var number = part.Kind == 'y' ? calendar.Year : part.Kind == 'm' ? calendar.Month : calendar.Day;
                        output.Append(number.ToString(part.Text.Length == 4 ? "D4" : part.Text.Length == 2 ? "D2" : "D1", CultureInfo.InvariantCulture));
                    }
                }

                return TextResult(output.ToString(), origin, span);
            }

            var syntax = pattern.ToString();
            int first = 0;
            while (first < syntax.Length && syntax[first] == 'L')
            {
                first++;
            }

            int last = syntax.Length;
            while (last > first && syntax[last - 1] == 'L')
            {
                last--;
            }

            bool percent = last > first && syntax[last - 1] == '%';
            var core = syntax.Substring(first, last - first - (percent ? 1 : 0));
            var dot = core.IndexOf('.');
            var integer = dot < 0 ? core : core.Substring(0, dot);
            var fraction = dot < 0 ? string.Empty : core.Substring(dot + 1);
            bool grouping = integer == "#,##0";
            bool validInteger = integer.Length > 0;
            if (!grouping)
            {
                foreach (var c in integer)
                {
                    validInteger &= c == '0';
                }
            }

            bool validFraction = dot < 0 || fraction.Length > 0;
            foreach (var c in fraction)
            {
                validFraction &= c == '0';
            }

            if (!validInteger || !validFraction)
            {
                return UnsupportedFormat(origin, span);
            }

            value *= percent ? 100 : 1;
            if (double.IsInfinity(value))
            {
                return FormulaValue.FromError("#NUM!");
            }

            value = DecimalRounding.Round(value, fraction.Length, "ROUND");
            var digits = Math.Max(grouping ? 1 : integer.Length, value == 0 || Math.Abs(value) < 1 ? 1 : (int)Math.Floor(Math.Log10(Math.Abs(value))) + 1);
            long literalLength = 0;
            foreach (var part in parts)
            {
                literalLength += part.Kind == 'l' ? part.Text.Length : 0;
            }

            long estimated = literalLength + digits + (grouping ? (digits - 1) / 3 : 0) + (dot < 0 ? 0 : 1L + fraction.Length) + (percent ? 1 : 0) + (value < 0 ? 1 : 0);
            CheckStringLength(estimated, origin, span);
            var formatted = Math.Abs(value).ToString("F" + fraction.Length.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            var split = formatted.IndexOf('.');
            var whole = split < 0 ? formatted : formatted.Substring(0, split);
            whole = whole.PadLeft(grouping ? 1 : integer.Length, '0');
            var result = new StringBuilder((int)estimated);
            if (value < 0)
            {
                result.Append('-');
            }

            for (var i = 0; i < first; i++)
            {
                result.Append(parts[i].Text);
            }

            for (var i = 0; i < whole.Length; i++)
            {
                Tick(origin, span);
                if (grouping && i > 0 && (whole.Length - i) % 3 == 0)
                {
                    result.Append(',');
                }

                result.Append(whole[i]);
            }

            if (split >= 0)
            {
                result.Append(formatted, split, formatted.Length - split);
            }

            if (percent)
            {
                result.Append('%');
            }

            for (var i = last; i < parts.Count; i++)
            {
                result.Append(parts[i].Text);
            }

            return TextResult(result.ToString(), origin, span);
        }

        private FormulaValue UnsupportedFormat(FormulaCellAddress origin, SourceSpan span)
        {
            Fail("UnsupportedFeature", "TEXT format is outside the supported numeric and date grammar.", origin, span, "TEXT");
            return default;
        }
    }
}

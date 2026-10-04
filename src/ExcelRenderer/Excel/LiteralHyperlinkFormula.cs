using System.Text;

namespace ExcelRenderer.Excel;

/// <summary>Recognizes only constant-string HYPERLINK calls; never evaluates expressions.</summary>
internal static class LiteralHyperlinkFormula
{
    /// <summary>Reads a literal HYPERLINK formula.</summary>
    /// <param name="formula">The original formula.</param>
    /// <param name="target">The literal location.</param>
    /// <param name="label">The optional literal friendly name.</param>
    /// <returns>Whether the whole formula is a supported call.</returns>
    internal static bool TryRead(string formula, out string target, out string? label)
    {
        target = string.Empty;
        label = null;
        var text = formula.Trim();
        if (text.StartsWith("=", StringComparison.Ordinal))
        {
            text = text.Substring(1).TrimStart();
        }

        if (text.StartsWith("_xlfn.", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(6);
        }

        if (!text.StartsWith("HYPERLINK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = 9;
        Skip();
        if (!Take('(') || !Read(out target))
        {
            return false;
        }

        Skip();
        if (Take(','))
        {
            if (!Read(out var friendly))
            {
                return false;
            }

            label = friendly;
        }

        Skip();
        if (!Take(')'))
        {
            return false;
        }

        Skip();
        return index == text.Length;

        void Skip()
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        bool Take(char c)
        {
            if (index >= text.Length || text[index] != c)
            {
                return false;
            }

            index++;
            return true;
        }

        bool Read(out string value)
        {
            value = string.Empty;
            Skip();
            if (!Take('"'))
            {
                return false;
            }

            var result = new StringBuilder();
            while (index < text.Length)
            {
                var c = text[index++];
                if (c == '"')
                {
                    if (index < text.Length && text[index] == '"')
                    {
                        index++;
                        result.Append('"');
                        continue;
                    }

                    value = result.ToString();
                    return true;
                }

                result.Append(c);
            }

            return false;
        }
    }
}

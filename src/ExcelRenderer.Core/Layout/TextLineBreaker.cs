using System.Globalization;

namespace ExcelRenderer.Core.Layout;

/// <summary>Shares Unicode element boundaries and exact wrap comparisons between products.</summary>
internal static class TextLineBreaker
{
    /// <summary>Enumerates whole Unicode text elements.</summary>
    /// <param name="text">The original text.</param>
    /// <returns>Unmodified text elements.</returns>
    internal static IEnumerable<string> Elements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return enumerator.GetTextElement();
        }
    }

    /// <summary>Tests the inclusive wrap boundary without expanding the available width.</summary>
    /// <param name="advance">The measured advance.</param>
    /// <param name="width">The available width.</param>
    /// <returns>Whether the text fits.</returns>
    internal static bool Fits(double advance, double width) => advance <= width;

    /// <summary>Breaks paragraphs while preserving explicit newlines and trailing spaces.</summary>
    /// <param name="text">The original text.</param>
    /// <param name="width">The point width.</param>
    /// <param name="wrap">Whether automatic wrapping is enabled.</param>
    /// <param name="measure">The physical-font advance measurement.</param>
    /// <returns>The finalized line strings and explicit break flags.</returns>
    internal static IEnumerable<(string Text, bool ExplicitBreak)> Break(string text, double width, bool wrap, Func<string, double> measure)
    {
        var paragraphs = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < paragraphs.Length; index++)
        {
            var paragraph = paragraphs[index];
            var explicitBreak = index < paragraphs.Length - 1;
            if (!wrap || width <= 0)
            {
                yield return (paragraph, explicitBreak);
                continue;
            }

            var current = string.Empty;
            foreach (var element in Elements(paragraph))
            {
                var candidate = current + element;
                if (current.Length > 0 && !Fits(measure(candidate), width))
                {
                    yield return (current, false);
                    current = element;
                }
                else
                {
                    current = candidate;
                }
            }

            yield return (current, explicitBreak);
        }
    }
}

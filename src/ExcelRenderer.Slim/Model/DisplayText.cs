using System.Text;

namespace ExcelRenderer.Slim.Model;

/// <summary>Reduces variation sequences to their base characters before measuring or drawing.</summary>
internal static class DisplayText
{
    /// <summary>Removes Unicode variation selectors while preserving all other UTF-16 code units.</summary>
    /// <param name="text">The display text.</param>
    /// <returns>The text with variation selectors removed.</returns>
    internal static string WithoutVariationSelectors(string text)
    {
        StringBuilder? result = null;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            var supplementarySelector = character == '\uDB40' && index + 1 < text.Length &&
                text[index + 1] >= '\uDD00' && text[index + 1] <= '\uDDEF';
            if ((character >= '\uFE00' && character <= '\uFE0F') || supplementarySelector)
            {
                result ??= new StringBuilder(text.Length).Append(text, 0, index);
                if (supplementarySelector)
                {
                    index++;
                }
            }
            else
            {
                result?.Append(character);
            }
        }

        return result?.ToString() ?? text;
    }
}

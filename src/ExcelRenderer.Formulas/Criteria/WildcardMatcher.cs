using System;
using System.Collections.Generic;
using System.Text;

namespace ExcelRenderer.Formulas.Criteria
{
    /// <summary>Compiles wildcard tokens for bounded, nonrecursive matching.</summary>
    internal sealed class WildcardMatcher
    {
        private readonly List<(int Position, byte Kind)> tokens = new List<(int Position, byte Kind)>();
        private readonly string pattern;

        /// <summary>Initializes a new instance of the <see cref="WildcardMatcher"/> class.</summary>
        /// <param name="pattern">Pattern with Excel wildcard escapes.</param>
        internal WildcardMatcher(string pattern)
        {
            this.pattern = pattern;
            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                if (c == '~' && i + 1 < pattern.Length && (pattern[i + 1] == '*' || pattern[i + 1] == '?' || pattern[i + 1] == '~'))
                {
                    tokens.Add((++i, 0));
                }
                else
                {
                    tokens.Add((i, (byte)(c == '*' ? 2 : c == '?' ? 1 : 0)));
                }
            }
        }

        /// <summary>Determines whether wildcard compilation or escape decoding is needed.</summary>
        /// <param name="text">Criterion or lookup text.</param>
        /// <returns>Whether special pattern characters occur.</returns>
        internal static bool HasPattern(string text) => text.IndexOf('*') >= 0 || text.IndexOf('?') >= 0 || text.IndexOf('~') >= 0;

        /// <summary>Extracts a literal exact key when the pattern has no wildcard tokens.</summary>
        /// <param name="literal">Decoded literal, including escaped wildcard characters.</param>
        /// <returns>Whether dictionary lookup is equivalent to pattern matching.</returns>
        internal bool TryGetLiteral(out string literal)
        {
            foreach (var token in tokens)
            {
                if (token.Kind != 0)
                {
                    literal = string.Empty;
                    return false;
                }
            }

            var builder = new StringBuilder(tokens.Count);
            foreach (var token in tokens)
            {
                builder.Append(pattern[token.Position]);
            }

            literal = builder.ToString();
            return true;
        }

        /// <summary>Matches text while charging every cursor step.</summary>
        /// <param name="text">Candidate text.</param>
        /// <param name="tick">Session work budget callback.</param>
        /// <returns>Whether the complete text matches.</returns>
        internal bool Matches(string text, Action tick)
        {
            int p = 0;
            int t = 0;
            int star = -1;
            int retry = 0;
            while (t < text.Length)
            {
                tick();
                if (p < tokens.Count && tokens[p].Kind == 2)
                {
                    star = p++;
                    retry = t;
                }
                else if (p < tokens.Count && (tokens[p].Kind == 1 || string.Compare(pattern, tokens[p].Position, text, t, 1, StringComparison.OrdinalIgnoreCase) == 0))
                {
                    p++;
                    t++;
                }
                else if (star >= 0)
                {
                    p = star + 1;
                    t = ++retry;
                }
                else
                {
                    return false;
                }
            }

            while (p < tokens.Count && tokens[p].Kind == 2)
            {
                tick();
                p++;
            }

            return p == tokens.Count;
        }
    }
}

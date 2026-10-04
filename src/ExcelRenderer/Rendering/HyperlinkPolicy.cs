using ExcelRenderer.Model;

namespace ExcelRenderer.Rendering;

/// <summary>Shared target safety and simple internal-reference resolution.</summary>
internal static class HyperlinkPolicy
{
    /// <summary>Validates and normalizes an external URI without visiting it.</summary>
    /// <param name="target">The original relationship target.</param>
    /// <param name="location">The optional relationship location.</param>
    /// <returns>The encoded absolute URI, or null when rejected.</returns>
    internal static string? External(string target, string? location)
    {
        if (target.Any(char.IsControl) || (location?.Any(char.IsControl) ?? false) ||
            !Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase))
        {
            return location is null && target.IndexOf("%0a", StringComparison.OrdinalIgnoreCase) < 0 &&
                target.IndexOf("%0d", StringComparison.OrdinalIgnoreCase) < 0 ? uri.AbsoluteUri : null;
        }

        if (uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(location))
        {
            var fragment = location!.StartsWith("#", StringComparison.Ordinal) ? location.Substring(1) : location;
            if (uri.Fragment.Length > 0 && !string.Equals(Uri.UnescapeDataString(uri.Fragment.Substring(1)), Uri.UnescapeDataString(fragment), StringComparison.Ordinal))
            {
                return null;
            }

            if (uri.Fragment.Length == 0)
            {
                uri = new UriBuilder(uri) { Fragment = fragment }.Uri;
            }
        }

        return uri.AbsoluteUri;
    }

    /// <summary>Resolves a simple internal A1 or scoped-name target.</summary>
    /// <param name="source">The source worksheet.</param>
    /// <param name="target">The raw location.</param>
    /// <param name="sheets">All original sheets.</param>
    /// <param name="sheet">The actual target sheet.</param>
    /// <param name="address">The target's top-left address, normalized for merges.</param>
    /// <param name="code">The failure diagnostic code.</param>
    /// <returns>Whether resolution succeeded.</returns>
    internal static bool Internal(
        ReportSheet source,
        string target,
        IReadOnlyList<ReportSheet> sheets,
        out ReportSheet? sheet,
        out CellAddress address,
        out string code)
    {
        sheet = null;
        address = default;
        code = "HyperlinkUnsupported";
        if (target.StartsWith("#", StringComparison.Ordinal))
        {
            target = target.Substring(1);
        }

        var nameSource = source;
        var nameKey = target;
        var quoted = false;
        var separator = -1;
        for (var i = 0; i < target.Length; i++)
        {
            if (target[i] == '\'')
            {
                if (quoted && i + 1 < target.Length && target[i + 1] == '\'')
                {
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (target[i] == '!' && !quoted)
            {
                separator = i;
            }
        }

        if (separator >= 0)
        {
            try
            {
                CellRangeParser.Parse(target.Substring(0, separator + 1) + "A1", out var namedSheet);
                var matchingSheet = sheets.FirstOrDefault(s => string.Equals(s.Name, namedSheet, StringComparison.OrdinalIgnoreCase));
                if (matchingSheet is null)
                {
                    code = "HyperlinkTargetOmitted";
                    return false;
                }

                nameSource = matchingSheet;
                nameKey = target.Substring(separator + 1);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        if (nameSource.HyperlinkNames.TryGetValue(nameKey, out var reference))
        {
            target = reference.StartsWith("=", StringComparison.Ordinal) ? reference.Substring(1) : reference;
            source = nameSource;
        }

        CellRange range;
        string? name;
        try
        {
            range = CellRangeParser.Parse(target, out name);
        }
        catch (ArgumentException)
        {
            return false;
        }

        sheet = sheets.FirstOrDefault(s => string.Equals(s.Name, name ?? source.Name, StringComparison.OrdinalIgnoreCase));
        code = "HyperlinkTargetOmitted";
        if (sheet is null)
        {
            return false;
        }

        address = range.First;
        var merged = sheet.MergedRanges.FirstOrDefault(r => r.Contains(range.First));
        if (merged != default)
        {
            address = merged.First;
        }

        return true;
    }
}

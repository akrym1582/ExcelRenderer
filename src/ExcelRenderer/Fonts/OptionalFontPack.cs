using System.Reflection;

namespace ExcelRenderer.Fonts;

/// <summary>Loads font resources only when the optional NuGet package is installed.</summary>
internal static class OptionalFontPack
{
    private static readonly Lazy<IReadOnlyList<Resource>> Resources = new(Load);

    /// <summary>Gets the font resources supplied by the optional font package.</summary>
    internal static IReadOnlyList<Resource> Fonts => Resources.Value;

    /// <summary>Loads a font from a neighboring assembly resource or from the ExcelRenderer assembly directory tree.</summary>
    /// <param name="name">The font file name.</param>
    /// <returns>The loaded font data, or <see langword="null"/> when the font cannot be found.</returns>
    internal static byte[]? LoadFontData(string name)
    {
        return FindLoader(name)?.Invoke();
    }

    private static Func<byte[]>? FindLoader(string name)
    {
        var directory = Path.GetDirectoryName(typeof(OptionalFontPack).Assembly.Location);
        var resource = FindResourceLoader(directory, name);
        if (resource is not null)
        {
            return resource;
        }

        var path = FindFontFile(directory, name);
        return path is null ? null : () => File.ReadAllBytes(path);
    }

    private static IReadOnlyList<Resource> Load()
    {
        var fonts = new List<Resource>();
        foreach (var name in new[] { "NotoSansJP-Regular.ttf", "ipamjm.ttf", "NotoColorEmoji.ttf" })
        {
            if (FindLoader(name) is { } loader)
            {
                var family = name switch
                {
                    "ipamjm.ttf" => "IPAmjMincho",
                    "NotoColorEmoji.ttf" => "Noto Color Emoji",
                    _ => "Noto Sans JP",
                };
                fonts.Add(new Resource(name, family, loader));
            }
        }

        return fonts;
    }

    private static Func<byte[]>? FindResourceLoader(string? assemblyDirectory, string name)
    {
        if (string.IsNullOrEmpty(assemblyDirectory) || !Directory.Exists(assemblyDirectory))
        {
            return null;
        }

        IEnumerable<string> assemblyPaths;
        try
        {
            assemblyPaths = Directory.EnumerateFiles(assemblyDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var assemblyPath in assemblyPaths)
        {
            try
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(resource =>
                        resource.Equals(name, StringComparison.Ordinal) ||
                        resource.EndsWith($".{name}", StringComparison.Ordinal));
                if (resourceName is null)
                {
                    continue;
                }

                return () =>
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidDataException($"Font resource is unavailable: {resourceName}");
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                };
            }
            catch (Exception)
            {
                // Ignore DLLs that cannot be loaded or inspected and continue searching.
            }
        }

        return null;
    }

    private static string? FindFontFile(string? assemblyDirectory, string name)
    {
        if (string.IsNullOrEmpty(assemblyDirectory) || !Directory.Exists(assemblyDirectory))
        {
            return null;
        }

        try
        {
            var options = new EnumerationOptions
            {
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
            };
            return Directory.EnumerateFiles(assemblyDirectory, name, options)
                .OrderBy(path => path, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Represents a font resource loaded from the optional font package.</summary>
    /// <param name="Name">The resource file name.</param>
    /// <param name="Family">The physical font family verified by font-pack tests.</param>
    /// <param name="Load">The deferred byte loader.</param>
    internal sealed record Resource(string Name, string Family, Func<byte[]> Load);
}

using System.Reflection;

namespace ExcelRenderer.Fonts;

/// <summary>Loads font resources only when the optional NuGet package is installed.</summary>
internal static class OptionalFontPack
{
    internal sealed record Resource(string Name, byte[] Data);

    private static readonly Lazy<IReadOnlyList<Resource>> Resources = new(Load);

    internal static IReadOnlyList<Resource> Fonts => Resources.Value;

    private static IReadOnlyList<Resource> Load()
    {
        Assembly assembly;
        try
        {
            assembly = Assembly.Load(new AssemblyName("ExcelRenderer.Fonts"));
        }
        catch (FileNotFoundException)
        {
            return [];
        }

        var fonts = new List<Resource>();
        foreach (var name in new[] { "NotoSansJP-Regular.ttf", "NotoSansCJKjp-Regular.otf", "NotoSerifCJKjp-Regular.otf", "ipamjm.ttf", "NotoColorEmoji.ttf" })
        {
            using var stream = assembly.GetManifestResourceStream($"ExcelRenderer.Fonts.{name}");
            if (stream is null)
            {
                throw new InvalidOperationException($"ExcelRenderer.Fonts is missing its font resource: {name}");
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            fonts.Add(new Resource(name, buffer.ToArray()));
        }

        return fonts;
    }
}

namespace ExcelRenderer.TestFixtures;

/// <summary>Opens immutable inputs captured from the documented parent revision.</summary>
internal static class RetainedWorkbook
{
    internal static MemoryStream Open(string name)
    {
        var input = new MemoryStream();
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RetainedWorkbooks", name));
        input.Write(bytes);input.Position = 0;return input;
    }

    internal static void Copy(string name, string destination) => File.Copy(Path.Combine(AppContext.BaseDirectory, "RetainedWorkbooks", name), destination, true);

    internal static void Copy(string name, Stream destination)
    {
        using var input = Open(name);
        input.CopyTo(destination);
    }
}

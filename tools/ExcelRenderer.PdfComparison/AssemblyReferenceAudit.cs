using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ExcelRenderer.PdfComparison;

/// <summary>Reads assembly metadata without loading product or native assemblies.</summary>
internal static class AssemblyReferenceAudit
{
    /// <summary>Audits every supplied managed PE file and prints a machine-readable inventory.</summary>
    /// <param name="listPath">A JSON array of absolute assembly paths.</param>
    internal static void Run(string listPath)
    {
        var paths = JsonSerializer.Deserialize<string[]>(File.ReadAllText(listPath)) ?? throw new InvalidDataException("Assembly path list required.");
        var results = new List<object>();
        foreach (var path in paths)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                continue;
            }

            var metadata = pe.GetMetadataReader();
            var references = metadata.AssemblyReferences.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).ToArray();
            if (references.Any(name => name.Contains("PdfSharp", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Forbidden assembly reference in {path}: {string.Join(", ", references)}");
            }

            results.Add(new { path, references });
        }

        Console.WriteLine(JsonSerializer.Serialize(results));
    }
}

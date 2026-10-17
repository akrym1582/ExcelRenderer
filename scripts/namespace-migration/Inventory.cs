using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

// Read metadata without loading dependencies or executing package code.
var assemblies = args.Select(path =>
{
    using var stream = File.OpenRead(path);
    using var pe = new PEReader(stream);
    var reader = pe.GetMetadataReader();
    var definition = reader.GetAssemblyDefinition();
    string FullName(TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var parent = type.GetDeclaringType();
        return parent.IsNil
            ? reader.GetString(type.Namespace) + "." + reader.GetString(type.Name)
            : FullName(parent) + "+" + reader.GetString(type.Name);
    }

    bool IsPublic(TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var visibility = type.Attributes & TypeAttributes.VisibilityMask;
        return visibility == TypeAttributes.Public ||
            (visibility == TypeAttributes.NestedPublic && IsPublic(type.GetDeclaringType()));
    }

    return new
    {
        assembly = reader.GetString(definition.Name),
        version = definition.Version.ToString(),
        types = reader.TypeDefinitions.Where(IsPublic).Select(FullName).Order().ToArray(),
        exportedTypes = reader.ExportedTypes.Select(handle =>
        {
            var type = reader.GetExportedType(handle);
            return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
        }).Order().ToArray(),
    };
}).ToArray();
Console.WriteLine(JsonSerializer.Serialize(assemblies, new JsonSerializerOptions { WriteIndented = true }));

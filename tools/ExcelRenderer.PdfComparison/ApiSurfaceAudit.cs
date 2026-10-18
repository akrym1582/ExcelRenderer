using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ExcelRenderer.PdfComparison;

/// <summary>Exports public signatures from metadata without loading old dependencies.</summary>
internal sealed class ApiSurfaceAudit : ISignatureTypeProvider<string, object?>
{
    /// <inheritdoc/>
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

    /// <inheritdoc/>
    public string GetByReferenceType(string elementType) => elementType + "&";

    /// <inheritdoc/>
    public string GetFunctionPointerType(MethodSignature<string> signature) => "method(" + string.Join(",", signature.ParameterTypes) + ")->" + signature.ReturnType;

    /// <inheritdoc/>
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";

    /// <inheritdoc/>
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;

    /// <inheritdoc/>
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;

    /// <inheritdoc/>
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType + (isRequired ? " modreq(" : " modopt(") + modifier + ")";

    /// <inheritdoc/>
    public string GetPinnedType(string elementType) => elementType + " pinned";

    /// <inheritdoc/>
    public string GetPointerType(string elementType) => elementType + "*";

    /// <inheritdoc/>
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

    /// <inheritdoc/>
    public string GetSZArrayType(string elementType) => elementType + "[]";

    /// <inheritdoc/>
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        var parent = type.GetDeclaringType();
        return parent.IsNil ? Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name)) : GetTypeFromDefinition(reader, parent, rawTypeKind) + "+" + reader.GetString(type.Name);
    }

    /// <inheritdoc/>
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + reader.GetString(type.Name)
            : Qualified(reader.GetString(type.Namespace), reader.GetString(type.Name));
    }

    /// <inheritdoc/>
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    /// <summary>Exports two built API surfaces and their added/removed signatures.</summary>
    /// <param name="before">The frozen baseline assembly.</param>
    /// <param name="after">The current assembly.</param>
    internal static void Compare(string before, string after)
    {
        var left = Read(before);
        var right = Read(after);
        Console.WriteLine(JsonSerializer.Serialize(new { before, after, removed = left.Except(right).Order().ToArray(), added = right.Except(left).Order().ToArray(), beforeCount = left.Count, afterCount = right.Count }));
    }

    private static string Qualified(string space, string name) => space.Length == 0 ? name : space + "." + name;

    private static HashSet<string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var provider = new ApiSurfaceAudit();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            if ((type.Attributes & TypeAttributes.VisibilityMask) is not (TypeAttributes.Public or TypeAttributes.NestedPublic))
            {
                continue;
            }

            var name = provider.GetTypeFromDefinition(reader, handle, 0);
            signatures.Add("type " + name);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) is not (MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem))
                {
                    continue;
                }

                var signature = method.DecodeSignature(provider, null);
                signatures.Add(name + "::" + reader.GetString(method.Name) + "`" + signature.GenericParameterCount + "(" + string.Join(",", signature.ParameterTypes) + ")->" + signature.ReturnType);
            }

            foreach (var fieldHandle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public)
                {
                    var constantHandle = field.GetDefaultValue();
                    var constant = constantHandle.IsNil ? string.Empty : "=" + Convert.ToHexString(reader.GetBlobBytes(reader.GetConstant(constantHandle).Value));
                    signatures.Add(name + "::" + reader.GetString(field.Name) + ":" + field.DecodeSignature(provider, null) + constant);
                }
            }
        }

        return signatures;
    }
}

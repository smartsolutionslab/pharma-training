using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Aspire.Hosting;

/// <summary>
/// The names of the types in a method's signature, as <see cref="Type.FullName"/> spells a type defined outside a generic
/// (<c>Namespace.Outer+Inner</c>), read from metadata alone: unlike <see cref="System.Reflection.MetadataLoadContext"/>, it
/// never resolves the assembly a type comes from, so a parameter whose assembly is not at hand still has a name.
/// Constructed types (generic instantiations, arrays, pointers) get a readable name that no integration event has.
/// </summary>
internal sealed class SignatureTypeNames(MetadataReader reader) : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => $"System.{typeCode}";

    public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaring = definition.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return $"{GetTypeFromDefinition(metadata, declaring, rawTypeKind)}+{name}";
        }

        var space = reader.GetString(definition.Namespace);
        return space.Length == 0 ? name : $"{space}.{name}";
    }

    public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return $"{GetTypeFromReference(metadata, (TypeReferenceHandle)reference.ResolutionScope, rawTypeKind)}+{name}";
        }

        var space = reader.GetString(reference.Namespace);
        return space.Length == 0 ? name : $"{space}.{name}";
    }

    public string GetTypeFromSpecification(MetadataReader metadata, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}[{string.Join(',', typeArguments)}]";

    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";

    public string GetSZArrayType(string elementType) => $"{elementType}[]";

    public string GetByReferenceType(string elementType) => $"{elementType}&";

    public string GetPointerType(string elementType) => $"{elementType}*";

    public string GetPinnedType(string elementType) => elementType;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetFunctionPointerType(MethodSignature<string> signature) => "method*";

    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
}

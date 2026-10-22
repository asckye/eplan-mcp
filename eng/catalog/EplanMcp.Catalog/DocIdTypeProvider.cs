using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace EplanMcp.Catalog;

/// <summary>
/// Spells signature types the way XML documentation comment IDs do (ECMA-334 annex D), so catalog IDs equal the
/// <c>member name</c> attributes in EPLAN's IntelliSense files. Roslyn drops every modifier; the C++/CLI files keep some
/// optional modifiers as <c>!</c> (a boxed value is <c>System.ValueType!Eplan.EplApi.Base.PointD</c>), and
/// <see cref="GetModifiedType"/> follows what they were measured to keep.
/// </summary>
internal sealed class DocIdTypeProvider : ISignatureTypeProvider<string, object?>
{
    public static readonly DocIdTypeProvider Instance = new();

    private const string CompilerServices = "System.Runtime.CompilerServices.";

    // PrimitiveTypeCode member names equal the System type names (Int32, String, Void, ...).
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        TypeName(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
        TypeName(reader, handle);

    public string GetTypeFromSpecification(
        MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) =>
        elementType + "[" + string.Join(",", Enumerable.Repeat("0:", shape.Rank)) + "]";

    public string GetByReferenceType(string elementType) => elementType + "@";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPinnedType(string elementType) => elementType + "^";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        StripArity(genericType) + "{" + string.Join(",", typeArguments) + "}";

    public string GetGenericTypeParameter(object? genericContext, int index) => "`" + index;

    public string GetGenericMethodParameter(object? genericContext, int index) => "``" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => modifier switch
    {
        _ when isRequired => unmodifiedType,
        // A C++ reference (T&) is a pointer marked IsImplicitlyDereferenced; the IntelliSense files spell it T.
        CompilerServices + "IsImplicitlyDereferenced" when unmodifiedType.EndsWith('*') => unmodifiedType[..^1],
        // The only compiler marker the IntelliSense files keep; IsConst, IsLong, IsBoxed and the rest are dropped.
        CompilerServices + "IsSignUnspecifiedByte" => unmodifiedType + "!" + modifier,
        _ when modifier.StartsWith(CompilerServices, StringComparison.Ordinal) => unmodifiedType,
        _ => unmodifiedType + "!" + modifier,
    };

    public string GetFunctionPointerType(MethodSignature<string> signature) =>
        "=FUNC:" + signature.ReturnType + "(" + string.Join(",", signature.ParameterTypes) + ")";

    /// <summary>Full name of a type definition; nested types are joined with a dot, as documentation IDs spell them.</summary>
    public static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        TypeDefinitionHandle outer = type.GetDeclaringType();
        string name = reader.GetString(type.Name);
        return !outer.IsNil ? TypeName(reader, outer) + "." + name : Qualify(reader.GetString(type.Namespace), name);
    }

    public static string TypeName(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? TypeName(reader, (TypeReferenceHandle)type.ResolutionScope) + "." + name
            : Qualify(reader.GetString(type.Namespace), name);
    }

    /// <summary>Name of a type handle of any kind (definition, reference or specification), or null for a nil handle.</summary>
    public static string? TypeName(MetadataReader reader, EntityHandle handle) => handle.IsNil ? null : handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeName(reader, (TypeDefinitionHandle)handle),
        HandleKind.TypeReference => TypeName(reader, (TypeReferenceHandle)handle),
        HandleKind.TypeSpecification => Instance.GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
        _ => null,
    };

    private static string Qualify(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

    // A generic instantiation drops the `n arity of the definition: List`1 becomes List{System.String}.
    private static string StripArity(string name)
    {
        int tick = name.LastIndexOf('`');
        return tick < 0 || name.IndexOf('.', tick) >= 0 ? name : name[..tick];
    }
}

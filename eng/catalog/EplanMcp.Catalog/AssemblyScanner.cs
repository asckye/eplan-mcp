using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace EplanMcp.Catalog;

/// <summary>What one assembly contributes: public entries, IDs of non-public members (to explain IntelliSense IDs that
/// are not API), and how many public types fell outside the namespace prefix (C++/CLI implementation types).</summary>
internal sealed record ScannedAssembly(
    string Name, string Version, List<CatalogEntry> Entries, HashSet<string> NonPublicIds, int OutsideTypes);

/// <summary>Reads the public surface of an assembly from its metadata tables, without loading or running it.</summary>
internal static partial class AssemblyScanner
{
    public static ScannedAssembly Scan(string path, string namespacePrefix)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        AssemblyDefinition definition = reader.GetAssemblyDefinition();
        var result = new ScannedAssembly(
            reader.GetString(definition.Name), definition.Version.ToString(), [], new HashSet<string>(StringComparer.Ordinal), 0);
        int outside = 0;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            string ns = OuterNamespace(reader, type);
            if (ns != namespacePrefix && !ns.StartsWith(namespacePrefix + ".", StringComparison.Ordinal))
            {
                outside += IsVisible(reader, type) ? 1 : 0;
                continue;
            }

            ScanType(reader, handle, ns, result);
        }

        return result with { OutsideTypes = outside };
    }

    private static void ScanType(MetadataReader reader, TypeDefinitionHandle handle, string ns, ScannedAssembly result)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string typeId = DocIdTypeProvider.TypeName(reader, handle);
        bool visible = IsVisible(reader, type);
        if (!visible)
        {
            result.NonPublicIds.Add("T:" + typeId);
        }

        string? baseType = DocIdTypeProvider.TypeName(reader, type.BaseType);
        string kind = (type.Attributes & TypeAttributes.Interface) != 0 ? "interface" : baseType switch
        {
            "System.Enum" => "enum",
            "System.ValueType" => "struct",
            "System.MulticastDelegate" => "delegate",
            _ => "class",
        };
        bool isSealed = (type.Attributes & TypeAttributes.Sealed) != 0;
        bool typeObsolete = IsObsolete(reader, type.GetCustomAttributes()); // members of an obsolete type are obsolete too
        TypeDefinitionHandle outer = type.GetDeclaringType();
        var common = (Assembly: result.Name, Namespace: ns, Type: "T:" + typeId);
        if (visible)
        {
            result.Entries.Add(new CatalogEntry
            {
                Id = "T:" + typeId,
                Kind = kind,
                Assembly = result.Name,
                Namespace = ns,
                Name = reader.GetString(type.Name),
                DeclaringType = outer.IsNil ? null : "T:" + DocIdTypeProvider.TypeName(reader, outer),
                Base = kind is "class" ? baseType : null,
                Interfaces = [.. type.GetInterfaceImplementations()
                    .Select(i => DocIdTypeProvider.TypeName(reader, reader.GetInterfaceImplementation(i).Interface)!)
                    .Order(StringComparer.Ordinal)],
                Modifiers = (type.Attributes & (TypeAttributes.Abstract | TypeAttributes.Sealed)) switch
                {
                    TypeAttributes.Abstract | TypeAttributes.Sealed => "static",
                    TypeAttributes.Abstract when kind is "class" => "abstract",
                    TypeAttributes.Sealed when kind is "class" => "sealed",
                    _ => null,
                },
                Obsolete = typeObsolete,
            });
        }

        var accessors = new HashSet<MethodDefinitionHandle>();
        foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
        {
            PropertyDefinition property = reader.GetPropertyDefinition(propertyHandle);
            PropertyAccessors pair = property.GetAccessors();
            accessors.UnionWith(new[] { pair.Getter, pair.Setter }.Where(a => !a.IsNil));
            var sig = property.DecodeSignature(DocIdTypeProvider.Instance, null);
            string name = reader.GetString(property.Name);
            string id = "P:" + typeId + "." + name + ParameterList(sig.ParameterTypes);
            string? get = Access(reader, pair.Getter, isSealed);
            string? set = Access(reader, pair.Setter, isSealed);
            if (!visible || (get is null && set is null))
            {
                result.NonPublicIds.Add(id);
                continue;
            }

            MethodDefinitionHandle any = pair.Getter.IsNil ? pair.Setter : pair.Getter;
            result.Entries.Add(Member(common, id, "property", name, get == "public" || set == "public" ? null : "protected",
                !sig.Header.IsInstance, sig.ReturnType, Parameters(reader, any, sig.ParameterTypes),
                string.Join(";", new[] { get is null ? null : "get", set is null ? null : "set" }.OfType<string>()),
                typeObsolete || IsObsolete(reader, property.GetCustomAttributes())));
        }

        foreach (EventDefinitionHandle eventHandle in type.GetEvents())
        {
            EventDefinition ev = reader.GetEventDefinition(eventHandle);
            EventAccessors pair = ev.GetAccessors();
            accessors.UnionWith(new[] { pair.Adder, pair.Remover, pair.Raiser }.Where(a => !a.IsNil));
            string name = reader.GetString(ev.Name);
            string id = "E:" + typeId + "." + name;
            if (!visible || Access(reader, pair.Adder, isSealed) is not { } access)
            {
                result.NonPublicIds.Add(id);
                continue;
            }

            result.Entries.Add(Member(common, id, "event", name, access == "public" ? null : access, false,
                DocIdTypeProvider.TypeName(reader, ev.Type), null, null, typeObsolete || IsObsolete(reader, ev.GetCustomAttributes())));
        }

        foreach (MethodDefinitionHandle methodHandle in type.GetMethods().Where(m => !accessors.Contains(m)))
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            var sig = method.DecodeSignature(DocIdTypeProvider.Instance, null);
            string name = reader.GetString(method.Name);
            // C++/CLI names the destructor ~T and the finalizer !T; the IntelliSense files call them Dispose and Finalize.
            string docName = name[0] switch { '~' => "Dispose", '!' => "Finalize", _ => name.Replace('.', '#') };
            string id = "M:" + typeId + "." + docName + (sig.GenericParameterCount > 0 ? "``" + sig.GenericParameterCount : "")
                + ParameterList(sig.ParameterTypes) + (name is "op_Implicit" or "op_Explicit" ? "~" + sig.ReturnType : "");
            if (!visible || Access(reader, methodHandle, isSealed) is not { } access)
            {
                result.NonPublicIds.Add(id);
                continue;
            }

            string methodKind = name switch
            {
                ".ctor" or ".cctor" => "constructor",
                "Dispose" => "lifecycle",
                "Finalize" when sig.ParameterTypes.Length == 0 => "lifecycle",
                _ when kind is "delegate" => "delegate_method",
                _ when (method.Attributes & MethodAttributes.SpecialName) != 0 && name.StartsWith("op_", StringComparison.Ordinal) => "operator",
                _ => "method",
            };
            result.Entries.Add(Member(common, id, methodKind, name, access == "public" ? null : access, !sig.Header.IsInstance,
                sig.ReturnType, Parameters(reader, methodHandle, sig.ParameterTypes), null,
                typeObsolete || IsObsolete(reader, method.GetCustomAttributes())));
        }

        // RTSpecialName: the value__ field of an enum.
        foreach (FieldDefinition field in type.GetFields().Select(reader.GetFieldDefinition).Where(f => (f.Attributes & FieldAttributes.RTSpecialName) == 0))
        {
            string name = reader.GetString(field.Name);
            string id = "F:" + typeId + "." + name;
            FieldAttributes access = field.Attributes & FieldAttributes.FieldAccessMask;
            if (!visible || !(access == FieldAttributes.Public || (!isSealed && access is FieldAttributes.Family or FieldAttributes.FamORAssem)))
            {
                result.NonPublicIds.Add(id);
                continue;
            }

            bool literal = (field.Attributes & FieldAttributes.Literal) != 0;
            result.Entries.Add(Member(common, id, kind is "enum" ? "enum_value" : literal ? "constant" : "field", name,
                access == FieldAttributes.Public ? null : "protected", (field.Attributes & FieldAttributes.Static) != 0 && kind is not "enum",
                kind is "enum" ? null : field.DecodeSignature(DocIdTypeProvider.Instance, null), null, null,
                typeObsolete || IsObsolete(reader, field.GetCustomAttributes()), Constant(reader, field.GetDefaultValue())));
        }
    }

    private static CatalogEntry Member(
        (string Assembly, string Namespace, string Type) common, string id, string kind, string name, string? access,
        bool isStatic, string? returns, IReadOnlyList<Parameter>? parameters, string? accessors, bool obsolete, string? value = null) => new()
        {
            Id = id,
            Kind = kind,
            Assembly = common.Assembly,
            Namespace = common.Namespace,
            Name = name,
            DeclaringType = common.Type,
            Access = access,
            Static = isStatic,
            Returns = returns,
            Parameters = parameters is { Count: > 0 } ? parameters : null,
            Accessors = accessors,
            Value = value,
            Obsolete = obsolete,
            NativeSignature = new[] { returns }.Concat(parameters?.Select(p => p.Type) ?? []).Any(IsNative),
        };

    private static string ParameterList(IReadOnlyCollection<string> types) =>
        types.Count == 0 ? "" : "(" + string.Join(",", types) + ")";

    private static List<Parameter> Parameters(MetadataReader reader, MethodDefinitionHandle method, IReadOnlyList<string> types)
    {
        var names = reader.GetMethodDefinition(method).GetParameters().Select(reader.GetParameter)
            .Where(p => p.SequenceNumber > 0).ToDictionary(p => p.SequenceNumber - 1, p => reader.GetString(p.Name));
        return [.. types.Select((t, i) => new Parameter(names.GetValueOrDefault(i, "p" + i.ToString(CultureInfo.InvariantCulture)), t))];
    }

    // "public", "protected" (visible to derived types of an unsealed type) or null.
    private static string? Access(MetadataReader reader, MethodDefinitionHandle handle, bool isSealed)
    {
        if (handle.IsNil)
        {
            return null;
        }

        return (reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.Family or MethodAttributes.FamORAssem when !isSealed => "protected",
            _ => null,
        };
    }

    private static bool IsVisible(MetadataReader reader, TypeDefinition type)
    {
        TypeDefinitionHandle outer = type.GetDeclaringType();
        return (type.Attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public => true,
            TypeAttributes.NestedPublic => IsVisible(reader, reader.GetTypeDefinition(outer)),
            TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem =>
                IsVisible(reader, reader.GetTypeDefinition(outer))
                && (reader.GetTypeDefinition(outer).Attributes & TypeAttributes.Sealed) == 0,
            _ => false,
        };
    }

    private static string OuterNamespace(MetadataReader reader, TypeDefinition type)
    {
        while (!type.GetDeclaringType().IsNil)
        {
            type = reader.GetTypeDefinition(type.GetDeclaringType());
        }

        return reader.GetString(type.Namespace);
    }

    private static bool IsObsolete(MetadataReader reader, CustomAttributeHandleCollection attributes) =>
        attributes.Select(reader.GetCustomAttribute).Any(a => a.Constructor.Kind switch
        {
            HandleKind.MemberReference => DocIdTypeProvider.TypeName(reader, reader.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent),
            HandleKind.MethodDefinition => DocIdTypeProvider.TypeName(reader, reader.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType()),
            _ => null,
        } == "System.ObsoleteAttribute");

    private static string? Constant(MetadataReader reader, ConstantHandle handle)
    {
        if (handle.IsNil)
        {
            return null;
        }

        Constant constant = reader.GetConstant(handle);
        return reader.GetBlobReader(constant.Value).ReadConstant(constant.TypeCode) switch
        {
            null => null,
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            object value => value.ToString(),
        };
    }

    // Native C++ types reach metadata as pointers, templates (managed generics are spelled with braces) or types outside
    // any namespace; C# cannot call such members.
    internal static bool IsNative(string? type) =>
        type is not null && (type.IndexOfAny(['*', '<']) >= 0 || TypeSeparators().Split(type).Any(
            t => t.Length > 0 && t[0] != '`' && !char.IsAsciiDigit(t[0]) && !t.Contains('.', StringComparison.Ordinal)));

    [GeneratedRegex(@"[{},\[\]@!^()=:~]+")]
    private static partial Regex TypeSeparators();
}

using System.Reflection;
using System.Reflection.Emit;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using EplanMcp.Bridge.Core.Server;
using EplanMcp.Contracts;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>
/// Model-facing text is English (architecture 8.11), so it contains no CJK characters: the tool contract, the server
/// instructions, the usage texts, and every string literal of the Host, Bridge.Core and Contracts, which is where
/// error messages, summaries and hints come from. Project data quoted in a message is not a literal and stays as it is.
/// An exception needs an entry with its reason in the lists below; an entry that no longer matches anything fails.
/// </summary>
[Trait("Size", "Small")]
[Collection(typeof(ToolBuildIsolation))]
public sealed partial class ModelTextTests
{
    /// <summary>CJK runs allowed in a tool's usage text.</summary>
    private static readonly (string Tool, string Text, string Reason)[] _allowedInUsage =
    [
        (PageTools.ListTool, "电源", "An argument value in an example: 'text' searches page descriptions, which are often Chinese."),
        (PageTools.GetTool, "标题页", "A sample value of 'typeName', which EPLAN gives in its user interface language."),
        (PageTools.GetTool, "封页", "A sample value of 'typeName', which EPLAN gives in its user interface language."),
    ];

    /// <summary>Types whose string literals may contain CJK (with their nested, compiler-generated types).</summary>
    private static readonly (string Type, string Reason)[] _allowedInLiterals =
    [
        ("EplanMcp.Host.Doctor.DoctorChecks", "doctor prints each check in Chinese and English for the engineer; the model never sees it."),
        ("EplanMcp.Host.Audit.AuditCommand", "audit verify prints its result in Chinese or English for the engineer; the model never sees it."),
    ];

    private static readonly JsonSerializerOptions _readableJson = new(McpJsonUtilities.DefaultOptions)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // IL opcodes by their (last) byte, to walk method bodies.
    private static readonly OpCode[] _oneByteOpCodes = OpCodeTable(size: 1);
    private static readonly OpCode[] _twoByteOpCodes = OpCodeTable(size: 2);

    [Fact]
    public void ToolContractAndServerInstructions_HaveNoCjk()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.ClearProviders());
        services.AddEplanMcp();
        using ServiceProvider provider = services.BuildServiceProvider();
        McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        string[] found =
        [
            .. Find("server instructions", options.ServerInstructions!),
            .. options.ToolCollection!.SelectMany(t => Find(t.ProtocolTool.Name, JsonSerializer.Serialize(t.ProtocolTool, _readableJson))),
        ];

        Assert.True(found.Length == 0, string.Join("\n", found));
    }

    [Fact]
    public void UsageTexts_HaveNoCjkButTheAllowedRuns()
    {
        var found = new List<string>();
        var used = new HashSet<(string, string)>();
        foreach ((string tool, IReadOnlyDictionary<string, string> sections) in ToolUsageCatalog.Usage)
        {
            foreach ((string topic, string text) in sections)
            {
                foreach (Match run in CjkRun().Matches(text))
                {
                    if (_allowedInUsage.Any(a => a.Tool == tool && a.Text == run.Value))
                    {
                        used.Add((tool, run.Value));
                    }
                    else
                    {
                        found.Add($"usage of {tool} ({topic}): '{run.Value}' in \"{Context(text, run)}\"");
                    }
                }
            }
        }

        Assert.True(found.Count == 0, string.Join("\n", found));
        Assert.All(_allowedInUsage, a => Assert.True(used.Contains((a.Tool, a.Text)), $"Allowed run '{a.Text}' is gone from the usage of {a.Tool}; remove the entry."));
    }

    [Fact]
    public void StringLiterals_HaveNoCjkOutsideTheAllowedTypes()
    {
        Assembly[] assemblies = [typeof(ServerInstructions).Assembly, typeof(BridgeServer).Assembly, typeof(BridgeError).Assembly];
        var found = new List<string>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type type in assemblies.SelectMany(a => a.GetTypes()))
        {
            string owner = Outermost(type).FullName!;
            foreach (MethodBase method in Methods(type))
            {
                foreach (string literal in Literals(method).Where(l => CjkRun().IsMatch(l)))
                {
                    if (_allowedInLiterals.Any(a => a.Type == owner))
                    {
                        used.Add(owner);
                    }
                    else
                    {
                        found.Add($"{type.FullName}.{method.Name}: \"{literal}\"");
                    }
                }
            }
        }

        Assert.True(found.Count == 0, string.Join("\n", found));
        Assert.All(_allowedInLiterals, a => Assert.True(used.Contains(a.Type), $"{a.Type} has no CJK literal any more; remove the entry."));
    }

    private static IEnumerable<string> Find(string where, string text) =>
        CjkRun().Matches(text).Select(run => $"{where}: '{run.Value}' in \"{Context(text, run)}\"");

    private static string Context(string text, Match run)
    {
        int start = Math.Max(0, run.Index - 40);
        return text[start..Math.Min(text.Length, run.Index + run.Length + 40)];
    }

    private static Type Outermost(Type type) => type.DeclaringType is { } outer ? Outermost(outer) : type;

    private static IEnumerable<MethodBase> Methods(Type type)
    {
        const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        return type.GetMethods(Declared).Concat<MethodBase>(type.GetConstructors(Declared));
    }

    /// <summary>The operands of the method's <c>ldstr</c> instructions: every string literal it uses, interpolation parts included.</summary>
    private static IEnumerable<string> Literals(MethodBase method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (int i = 0; i < il.Length;)
        {
            OpCode code = il[i] == 0xFE ? _twoByteOpCodes[il[i + 1]] : _oneByteOpCodes[il[i]];
            i += code.Size > 0 ? code.Size : throw new InvalidDataException($"Unknown IL opcode 0x{il[i]:X2} in {method.DeclaringType}.{method.Name}.");
            if (code == OpCodes.Ldstr)
            {
                yield return method.Module.ResolveString(BitConverter.ToInt32(il, i));
            }

            i += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                _ => 4,
            };
        }
    }

    private static OpCode[] OpCodeTable(int size)
    {
        var table = new OpCode[256];
        foreach (OpCode code in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (OpCode)f.GetValue(null)!).Where(c => c.Size == size))
        {
            table[(ushort)code.Value & 0xFF] = code;
        }

        return table;
    }

    /// <summary>
    /// CJK radicals to CJK unified ideographs (U+2E80-9FFF: CJK punctuation, kana, bopomofo and extension A included),
    /// Hangul syllables, CJK compatibility ideographs and forms, full-width forms, and extensions B and later
    /// (surrogate pairs from U+20000).
    /// </summary>
    [GeneratedRegex(@"(?:[⺀-鿿가-힯豈-﫿︰-﹏＀-￯]|[\uD840-\uD8BF][\uDC00-\uDFFF])+")]
    private static partial Regex CjkRun();
}

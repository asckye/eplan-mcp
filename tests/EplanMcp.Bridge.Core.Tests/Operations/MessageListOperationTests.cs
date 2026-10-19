using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class MessageListOperationTests
{
    // EPLAN's MessageLevel numbers (reflection), plus 7, the "new version" note S6 saw outside the enum.
    private const int Trace = 0;
    private const int Note = 1;
    private const int Warning = 2;
    private const int AssertLevel = 3;
    private const int Error = 4;
    private const int Fatal = 5;
    private const int OutsideTheEnum = 7;

    private readonly FakeSystemMessageReader _reader = new();

    [Fact]
    public void Execute_ByDefault_ListsWarningsAndAboveNewestFirst()
    {
        _reader.Messages.AddRange(
        [
            Message(Warning, "oldest warning"),
            Message(Trace, "trace"),
            Message(Note, "note"),
            Message(Error, "error"),
            Message(OutsideTheEnum, "new version"),
            Message(Fatal, "fatal"),
            Message(AssertLevel, "assert"),
        ]);

        MessageListData data = Run(new MessageListArgs { Limit = 50 });

        Assert.Equal(
            [("warning", "assert"), ("fatal", "fatal"), ("error", "error"), ("warning", "oldest warning")],
            data.Items.Select(m => (m.Level, m.Text)));
        Assert.Equal(4, data.Total);
        Assert.Equal(1, _reader.Reads);
    }

    [Fact]
    public void Execute_MinLevelError_KeepsErrorsAndFatalErrors()
    {
        _reader.Messages.AddRange([Message(Fatal, "f"), Message(Warning, "w"), Message(Error, "e"), Message(AssertLevel, "a")]);

        MessageListData data = Run(new MessageListArgs { MinLevel = SystemMessage.Error, Limit = 50 });

        Assert.Equal(["e", "f"], data.Items.Select(m => m.Text));
    }

    [Fact]
    public void Execute_MinLevelMessage_AddsNotesAndUnnamedLevelsButNeverTrace()
    {
        _reader.Messages.AddRange([Message(Trace, "t"), Message(Note, "n"), Message(OutsideTheEnum, "v"), Message(-1, "odd")]);

        MessageListData data = Run(new MessageListArgs { MinLevel = SystemMessage.Message, Limit = 50 });

        Assert.Equal([("message", "odd"), ("message", "v"), ("message", "n")], data.Items.Select(m => (m.Level, m.Text)));
    }

    [Fact]
    public void Execute_NumberS000000OrNone_IsNull()
    {
        _reader.Messages.AddRange(
        [
            Message(Error, "a", index: "S085016", occurrences: 3),
            Message(Error, "b", index: MessageListOperation.NoNumber),
            Message(Error, "c", index: string.Empty),
        ]);

        MessageListData data = Run(new MessageListArgs { Limit = 50 });

        Assert.Equal([null, null, "S085016"], data.Items.Select(m => m.Number));
        Assert.Equal([1, 1, 3], data.Items.Select(m => m.Occurrences));
    }

    [Fact]
    public void Execute_Slices_AndCountsEveryMatch_ReadingOnce()
    {
        _reader.Messages.AddRange(Enumerable.Range(1, 7).Select(i => Message(Error, "e" + i)));
        _reader.Messages.Insert(3, Message(Note, "not counted"));

        MessageListData data = Run(new MessageListArgs { Offset = 2, Limit = 3 });

        Assert.Equal(["e5", "e4", "e3"], data.Items.Select(m => m.Text));
        Assert.Equal(7, data.Total);
        Assert.Equal(1, _reader.Reads);
    }

    [Fact]
    public void Execute_NoMessages_IsAnEmptyList()
    {
        MessageListData data = Run(new MessageListArgs { Limit = 20 });

        Assert.Empty(data.Items);
        Assert.Equal(0, data.Total);
    }

    [Fact]
    public void Execute_OffsetPastTheMatches_IsInvalidArgument()
    {
        _reader.Messages.Add(Message(Error, "e"));

        Assert.Equal(ErrorReason.InvalidArgument, Fails(new MessageListArgs { Offset = 5, Limit = 5 }).Reason);
    }

    [Theory]
    [InlineData("fatal")]
    [InlineData("Error")]
    [InlineData("")]
    public void Execute_UnknownMinLevel_IsInvalidArgumentBeforeAnythingIsRead(string level)
    {
        BridgeError error = Fails(new MessageListArgs { MinLevel = level, Limit = 5 });

        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        Assert.Contains("'min_level'", error.Metadata!["violations"], StringComparison.Ordinal);
        Assert.Equal(0, _reader.Reads);
    }

    [Theory]
    [InlineData(Trace, null)]
    [InlineData(Note, "message")]
    [InlineData(Warning, "warning")]
    [InlineData(AssertLevel, "warning")]
    [InlineData(Error, "error")]
    [InlineData(Fatal, "fatal")]
    [InlineData(6, "message")]
    [InlineData(OutsideTheEnum, "message")]
    public void LevelName_MapsEveryEplanLevel(int eplanLevel, string? expected)
    {
        Assert.Equal(expected, MessageListOperation.LevelName(eplanLevel));
    }

    [Fact]
    public void Router_ParsesCamelCaseArgumentsAndReturnsCamelCaseData()
    {
        _reader.Messages.AddRange([Message(Error, "Datei nicht gefunden", index: "S085016"), Message(Warning, "w")]);
        var router = new OperationRouter([new MessageListOperation(_reader)], new InlineDispatcher(), null, new ListLog(), TimeSpan.FromSeconds(10));

        JToken data = router.Invoke(OperationNames.MessageList, JObject.Parse("""{"minLevel":"error","offset":0,"limit":10}"""), "c");

        Assert.Equal(1, (int)data["total"]!);
        Assert.Equal("S085016", (string?)data["items"]![0]!["number"]);
        Assert.Equal("error", (string?)data["items"]![0]!["level"]);
    }

    private static SystemMessageRecord Message(int level, string text, string index = MessageListOperation.NoNumber, int occurrences = 1) =>
        new() { Level = level, Text = text, Index = index, Occurrences = occurrences };

    private MessageListData Run(MessageListArgs args) => (MessageListData)new MessageListOperation(_reader).Execute(args)!;

    private BridgeError Fails(MessageListArgs args) => Assert.Throws<BridgeErrorException>(() => Run(args)).Error;

    /// <summary>EPLAN's system messages in its enumeration order, oldest first; counts how often the collection is read.</summary>
    private sealed class FakeSystemMessageReader : ISystemMessageReader
    {
        public List<SystemMessageRecord> Messages { get; } = [];

        public int Reads { get; private set; }

        public IReadOnlyList<SystemMessageRecord> ReadAll()
        {
            Reads++;
            return [.. Messages];
        }
    }
}

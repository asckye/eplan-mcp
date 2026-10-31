using System.Globalization;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

[Trait("Size", "Small")]
public sealed class WriteChangeLinesTests
{
    [Theory]
    [InlineData("create", "page", null, null, null, "create page item")]
    [InlineData("create", "page", null, null, "Circuit", "create page item (Circuit)")]
    [InlineData("create", "page", null, null, "", "create page item ()")]
    [InlineData("delete", "page", null, null, null, "delete page item")]
    [InlineData("delete", "page", null, "Circuit, 3 functions", null, "delete page item (Circuit, 3 functions)")]
    [InlineData("delete", "page", null, "", null, "delete page item ()")]
    [InlineData("create", "parts", null, null, "1", "add 1 part from item to the parts database")]
    [InlineData("create", "parts", null, null, "2", "add 2 parts from item to the parts database")]
    [InlineData("update", "parts", null, null, null, "update ? parts of the parts database from item")]
    [InlineData("update", "parts", null, null, "01", "update 01 parts of the parts database from item")]
    [InlineData("update", "project", "stored parts", "12", "1", "store 1 part the project references in project item (now 12)")]
    [InlineData("update", "page", "connections", "7", null, "generate connections on page item (now 7; may add or remove connections)")]
    [InlineData("update", "project", "check messages", "0", null, "run check on ? pages (EPLAN's current scheme), replacing the 0 existing check messages")]
    [InlineData("update", "project", "reports", "2", null, "update all reports (2 report pages now); may create, replace or delete report pages")]
    [InlineData("update", "page", "report", null, null, "update report page item")]
    [InlineData("update", "device", "device tags", "3 devices", null, "renumber 3 devices")]
    [InlineData("update", "page", "page number", null, null, "renumber page item")]
    [InlineData("update", "terminal", "numbering", "12 terminals", null, "renumber 12 terminals")]
    [InlineData("update", "project", "PLC bus data", "2 boxes", null, "import PLC bus data: 2 boxes")]
    [InlineData("update", "project", "translations", "4 texts", null, "translate 4 texts")]
    [InlineData("update", "placeholder", "value set", null, "mA", "apply value set 'mA' to placeholder item")]
    [InlineData("update", "device", "tag", "-K1", "-K2", "update device item: tag '-K1' -> '-K2'")]
    [InlineData("unknown", "custom", null, null, null, "update custom item:  '' -> ''")]
    [InlineData("create", "parts", "list", null, "1", "import parts list into project item from  ( bytes, SHA-256 ); converter ; before ")]
    [InlineData("create", "page", "report", null, null, "create page item")]
    [InlineData("read", "unverified API", "Page.Name", null, null, "read unverified API item; Page.Name")]
    [InlineData("write", "unverified API", "Page.Name", null, "\"new\"", "write unverified API item = \"new\"; Page.Name")]
    public void For_RepresentativeChanges_PreservesExactWording(string action, string kind, string? property, string? oldValue, string? newValue, string expected)
    {
        var plan = new WritePlan
        {
            Changes = [new PlannedChange { Action = action, Kind = kind, Name = "item", Property = property, OldValue = oldValue, NewValue = newValue }],
        };

        Assert.Equal([expected], WriteChangeLines.For(plan));
    }

    [Fact]
    public void For_PlanDetails_PreservesListAndCheckOrder()
    {
        var plan = new WritePlan
        {
            Changes =
            [
                new PlannedChange { Action = "update", Kind = "parts", Name = "Line1", Property = "list" },
                new PlannedChange { Action = "update", Kind = "project", Property = "check messages", OldValue = "3" },
            ],
            Details = new() { ["file"] = "incoming/parts.xml", ["bytes"] = "1234", ["sha256"] = "abc123", ["converter"] = "Xml", ["before"] = "12 parts", ["pageCount"] = "321", ["scheme"] = "Mine" },
        };

        Assert.Equal(
            ["import parts list into project Line1 from incoming/parts.xml (1234 bytes, SHA-256 abc123); converter Xml; before 12 parts", "run check on 321 pages (scheme: Mine), replacing the 3 existing check messages"],
            WriteChangeLines.For(plan));
    }

    [Fact]
    public void For_EmptyChanges_ProducesNoLines() => Assert.Empty(WriteChangeLines.For(new WritePlan()));

    [Fact]
    public void For_ManyChanges_PreservesEveryLineInOrder()
    {
        string[] names = [.. Enumerable.Range(0, 1001).Select(i => i.ToString(CultureInfo.InvariantCulture))];
        var plan = new WritePlan { Changes = [.. names.Select(name => new PlannedChange { Action = "create", Kind = "page", Name = name })] };

        Assert.Equal(names.Select(name => "create page " + name), WriteChangeLines.For(plan));
    }

    [Fact]
    public void For_RawValues_PreservesLongTextAndSpecialCharacters()
    {
        string text = new string('x', 240) + "'\"\r\n\t\0\u202e\u2066\\&<é>\U0001f600";
        var plan = new WritePlan
        {
            Changes =
            [
                new PlannedChange { Action = "create", Kind = "page", Name = text, NewValue = text },
                new PlannedChange { Action = "delete", Kind = "page", Name = text, OldValue = text },
                new PlannedChange { Action = "update", Kind = "device", Name = text, Property = text, OldValue = text, NewValue = text },
            ],
        };

        Assert.Equal(
            [$"create page {text} ({text})", $"delete page {text} ({text})", $"update device {text}: {text} '{text}' -> '{text}'"],
            WriteChangeLines.For(plan));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("zh-CN")]
    [InlineData("ar-SA")]
    public void For_Action_PreservesCleaningSurrogateTruncationAndCulture(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var plan = new WritePlan
            {
                Changes =
                [
                    new PlannedChange
                    {
                        Action = "run", Kind = "action", Name = "Act\n\u202eX",
                        NewValue = JsonSerializer.Serialize(new[]
                        {
                            new ActionParameter { Name = "PROJECTNAME", Value = @"C:\Projects\Line1.elk" },
                            new ActionParameter { Name = "TEXT\n\u202e", Value = new string('x', 119) + "\U0001f600y" },
                        }),
                    },
                ],
                Details = new() { ["effects"] = "one\n\u202e;two\t" },
            };

            Assert.Equal([$"Write Act X: PROJECTNAME=Line1.elk; TEXT ={new string('x', 119)}… (122 characters); one ; two "], WriteChangeLines.For(plan));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData(120, "")]
    [InlineData(121, "… (121 characters)")]
    public void For_ActionValue_PreservesTruncationBoundary(int length, string suffix)
    {
        var plan = new WritePlan
        {
            Changes = [new PlannedChange { Action = "run", Kind = "action", Name = "Act", NewValue = JsonSerializer.Serialize(new[] { new ActionParameter { Name = "TEXT", Value = new string('x', length) } }) }],
        };

        Assert.Equal([$"Write Act: TEXT={new string('x', 120)}{suffix}; "], WriteChangeLines.For(plan));
    }

    [Fact]
    public void For_CableGeneration_PreservesSchemeFormatting()
    {
        var plan = new WritePlan
        {
            Changes = [new PlannedChange { Action = "update", Kind = "cables", Property = "generation" }],
            Details = new() { ["creationScheme"] = "A\n\u202eB", ["numberingScheme"] = new string('n', 121), ["selectionScheme"] = "S" },
        };

        Assert.Equal(
            [$"Generate or update automatic cables in this project using creation A B, numbering {new string('n', 120)}… (121 characters), selection S. Existing cable names will be preserved; existing cable renumbering is outside this operation. New cables and selection effects cannot be predicted in advance. A project backup will be made first. Undo after commit has not been verified."],
            WriteChangeLines.For(plan));
    }

    [Fact]
    public void For_ActionAndCablesWithoutDetails_PreservesFallbacks()
    {
        var plan = new WritePlan
        {
            Changes =
            [
                new PlannedChange { Action = "run", Kind = "action", Name = "Act" },
                new PlannedChange { Action = "update", Kind = "cables", Property = "generation" },
            ],
        };

        Assert.Equal(
            ["Write Act: ; ", "Generate or update automatic cables in this project using creation ?, numbering ?, selection ?. Existing cable names will be preserved; existing cable renumbering is outside this operation. New cables and selection effects cannot be predicted in advance. A project backup will be made first. Undo after commit has not been verified."],
            WriteChangeLines.For(plan));
    }
}

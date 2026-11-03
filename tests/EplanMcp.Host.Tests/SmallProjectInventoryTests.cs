using System.Globalization;
using System.Text.Json;
using EplanMcp.Host.Tests.Safety;
using EplanMcp.TestSupport;

namespace EplanMcp.Host.Tests;

[Trait("Size", "Small")]
public sealed class SmallProjectInventoryTests
{
    private const string Project = "InventoryProject";
    private const string CircuitPage = "=GAA+A1&EFS1/1";
    private const string SecondCircuitPage = "=GAA+A1&EFS1/2";
    private static readonly int[] _symbolPropertyIds = [20111, 20112, 20113];
    // These are the exact selected pages of the 2026-11-03 slot B timeout. Responses below are synthetic.
    private static readonly string[] _pageNames =
    [
        "&AAA1/1", "&AAB1/1", CircuitPage, SecondCircuitPage,
        "=K1+A2&EFS1/1", "=K1+A2&EFS1/2", "=K1+A2&EFA1/1", "=K1+A2&EFA1/2",
        "=K1+A2&EFA1/3", "=K1+A2&EFA1/4", "=K1+A2&EFA1/5", "=K1+A2&EFA1/6",
        "=K1+A2&EFA1/7", "+A1&EMA2/2", "+A1&EPA1/1", "+A1&EMB1/1",
    ];

    [Fact]
    public async Task SlotB_SelectedSixteenPages_PersistsEveryPageWithBoundedReadCalls()
    {
        var fake = new InventoryReader();
        fake.BeforeCall = (_, _) => fake.Time.Advance(TimeSpan.FromSeconds(1));
        await fake.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_pageNames, fake.Saved[^1].EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(Enumerable.Range(1, 16), fake.Saved.Select(s => s.GetArrayLength()));
        Assert.InRange(fake.Time.Now - DateTimeOffset.UnixEpoch, TimeSpan.Zero, TimeSpan.FromMinutes(20));
        Assert.InRange(fake.Calls.Count, 32, 250);
        Assert.Equal(16, fake.Calls.Count(c => c.Tool == "eplan_page_get"));
        Assert.Equal(16, fake.Calls.Count(c => c.Tool == "eplan_device_list"));
        Assert.DoesNotContain(fake.Calls, c => c.Tool == "eplan_object_get");
        Assert.All(fake.Calls.Where(c => c.Tool == "eplan_object_find"), c =>
        {
            string page = c.Args.GetProperty("page").GetString()!;
            Assert.Contains(SmallProjectInventory.Pages, p => p.Name == page && p.Type is "Circuit" or "Overview");
            Assert.Equal(200, c.Args.GetProperty("limit").GetInt32());
        });
        Assert.All(fake.Calls.Where(c => c.Tool == "eplan_device_list"), c => Assert.True(c.Args.GetProperty("include_auxiliary").GetBoolean()));
        Assert.Equal(16, fake.Log.Count(line => line.Contains("Inventory ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task DrawingPages_RetainsGraphicsPropertyPlacementsAndFormWithoutObjectGets()
    {
        var fake = new InventoryReader();
        fake.Respond = (tool, args) => tool == "eplan_page_get" && Page(args) == "&AAA1/1"
            ? PageResult("&AAA1/1", args.TryGetProperty("drawing_cursor", out _)
                ? [Drawing("Rectangle", "&AAA1/1", 3), Drawing("PropertyPlacement", "&AAA1/1", 1, new() { ["property_id"] = "20111" })]
                : [Drawing("Line", "&AAA1/1", 1), Drawing("Text", "&AAA1/1", 2, new() { ["text"] = "Synthetic title" })],
                args.TryGetProperty("drawing_cursor", out _) ? null : "drawing-2")
            : null;
        await fake.RunAsync(TestContext.Current.CancellationToken);

        JsonElement page = fake.Saved[^1][0];
        Assert.Equal("SyntheticForm", page.GetProperty("form").GetString());
        Assert.Equal(1, page.GetProperty("objectCounts").GetProperty("Line").GetInt32());
        Assert.Equal(1, page.GetProperty("objectCounts").GetProperty("Rectangle").GetInt32());
        Assert.Equal(1, page.GetProperty("graphics").GetProperty("texts").GetInt32());
        Assert.Equal("Synthetic title", page.GetProperty("graphics").GetProperty("textSamples")[0].GetString());
        Assert.Equal(1, page.GetProperty("propertyPlacementsById").GetProperty("20111").GetInt32());
        Assert.True(page.GetProperty("drawingPagesComplete").GetBoolean());
        Assert.DoesNotContain(fake.Calls, c => c.Tool == "eplan_object_get");
        Assert.All(fake.Calls.Where(c => c.Tool == "eplan_page_get"), c =>
        {
            Assert.True(c.Args.GetProperty("include_drawings").GetBoolean());
            Assert.Equal("detailed", c.Args.GetProperty("response_format").GetString());
        });
    }

    [Fact]
    public async Task SymbolIdentity_BatchesOnlyThreePropertiesAndReusesLibraryNameSearchAcrossPages()
    {
        var fake = new InventoryReader();
        fake.Respond = (tool, args) => tool == "eplan_page_get" && Page(args) is CircuitPage or SecondCircuitPage
            ? PageResult(Page(args), Enumerable.Range(1, 110).Select(i => Drawing("Function", Page(args), i,
                new() { ["name"] = "-K" + i.ToString(CultureInfo.InvariantCulture) })).ToArray())
            : null;
        await fake.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(fake.Calls, c => c.Tool == "eplan_symbol_search");
        var propertyCalls = fake.Calls.Where(c => c.Tool == "eplan_property_get").ToArray();
        Assert.NotEmpty(propertyCalls);
        Assert.All(propertyCalls, c =>
        {
            JsonElement queries = c.Args.GetProperty("queries");
            Assert.InRange(queries.GetArrayLength(), 1, 50);
            Assert.All(queries.EnumerateArray(), q =>
            {
                Assert.Equal(_symbolPropertyIds, q.GetProperty("properties").EnumerateArray().Select(p => p.GetProperty("property_id").GetInt32()));
                JsonElement locator = q.GetProperty("target").GetProperty("locator");
                Assert.Equal("function", locator.GetProperty("kind").GetString());
                Assert.False(locator.TryGetProperty("name", out _));
                Assert.False(locator.TryGetProperty("digest", out _));
            });
        });
        Assert.DoesNotContain(fake.Calls, c => c.Tool == "eplan_object_get");
        JsonElement page = fake.Saved[^1][2];
        Assert.Equal(110, page.GetProperty("symbolReferences").GetArrayLength());
        Assert.All(page.GetProperty("symbolReferences").EnumerateArray(), r =>
        {
            Assert.Equal("IEC_symbol", r.GetProperty("library").GetString());
            Assert.Equal("K", r.GetProperty("symbol").GetString());
            Assert.Equal(JsonValueKind.Object, r.GetProperty("definition").ValueKind);
        });
    }

    [Fact]
    public async Task DeviceParts_UsesOnlyThirtyDistinctMainTagsAndCachesAcrossPages()
    {
        var fake = new InventoryReader();
        fake.Respond = (tool, args) => tool == "eplan_device_list" && Page(args) is CircuitPage or SecondCircuitPage
            ? Json(new
            {
                devices = Enumerable.Range(1, 36).Select(i => new { tag = "-K" + i.ToString(CultureInfo.InvariantCulture), main = i <= 35, category = "Coil", page = Page(args), parts = 1 }).ToArray(),
                nextCursor = (string?)null,
                truncated = false,
            })
            : null;
        await fake.RunAsync(TestContext.Current.CancellationToken);

        var reads = fake.Calls.Where(c => c.Tool == "eplan_device_get").ToArray();
        Assert.Equal(30, reads.Length);
        Assert.Equal(30, reads.Select(c => c.Args.GetProperty("tag").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(reads, c => c.Args.GetProperty("tag").GetString() == "-K36");
        JsonElement page = fake.Saved[^1][2];
        Assert.Equal(30, page.GetProperty("deviceDetails").GetArrayLength());
        Assert.Contains(page.GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("30", StringComparison.Ordinal));
        Assert.Equal(30, page.GetProperty("devicePartNumberCounts").GetProperty("SyntheticPart").GetInt32());
    }

    [Fact]
    public async Task FunctionCategories_KeepMainAuxiliaryAndInterruptionEvidenceWithoutInventingPropertyTargets()
    {
        var fake = new InventoryReader();
        fake.Respond = (tool, args) =>
        {
            if (tool == "eplan_page_get" && Page(args) == CircuitPage)
            {
                return PageResult(CircuitPage,
                [
                    Drawing("Function", CircuitPage, 1, new() { ["name"] = "-K1" }),
                    Drawing("Terminal", CircuitPage, 2, new() { ["name"] = "-X1:1" }),
                    Drawing("InterruptionPoint", CircuitPage, 3, new() { ["name"] = "L1" }),
                ]);
            }
            if (tool == "eplan_device_list" && Page(args) == CircuitPage)
            {
                return Json(new
                {
                    devices = new[]
                    {
                        new { tag = "-K1", main = true, category = "Coil", page = CircuitPage, parts = 1 },
                        new { tag = "-X1:1", main = false, category = "PLCTerminal", page = CircuitPage, parts = 0 },
                    },
                    nextCursor = (string?)null,
                    truncated = false,
                });
            }
            return null;
        };
        await fake.RunAsync(TestContext.Current.CancellationToken);

        JsonElement page = fake.Saved[^1][2];
        Assert.Equal(1, page.GetProperty("functionCounts").GetProperty("Coil/main").GetInt32());
        Assert.Equal(1, page.GetProperty("functionCounts").GetProperty("PLCTerminal/auxiliary").GetInt32());
        Assert.Equal(2, page.GetProperty("functions").GetArrayLength());
        Assert.Equal(3, page.GetProperty("objects").GetArrayLength());
        Assert.Equal(1, page.GetProperty("specials").GetProperty("plcPoints").GetProperty("count").GetInt32());
        Assert.Equal(1, page.GetProperty("specials").GetProperty("terminalsAndStrips").GetProperty("count").GetInt32());
        Assert.Equal(1, page.GetProperty("specials").GetProperty("interruptionPoints").GetProperty("count").GetInt32());
        JsonElement[] references = page.GetProperty("symbolReferences").EnumerateArray().ToArray();
        Assert.Equal(3, references.Length);
        Assert.Equal(2, references.Count(r => r.GetProperty("library").GetString() == "IEC_symbol"));
        JsonElement interruption = Assert.Single(references, r => r.GetProperty("Kind").GetString() == "InterruptionPoint");
        Assert.Equal(JsonValueKind.Null, interruption.GetProperty("library").ValueKind);
        Assert.Contains(page.GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("InterruptionPoint", StringComparison.Ordinal)
            && g.GetString()!.Contains("selective property reader", StringComparison.Ordinal));
        var properties = Assert.Single(fake.Calls, c => c.Tool == "eplan_property_get");
        Assert.Equal(Enumerable.Range(1, 2), properties.Args.GetProperty("queries").EnumerateArray()
            .Select(q => q.GetProperty("target").GetProperty("locator").GetProperty("oid").GetInt32()));
        Assert.All(properties.Args.GetProperty("queries").EnumerateArray(), q =>
            Assert.Equal("function", q.GetProperty("target").GetProperty("locator").GetProperty("kind").GetString()));
        Assert.DoesNotContain(fake.Calls, c => c.Tool == "eplan_object_get");
    }

    [Fact]
    public async Task MissingDrawingKind_CapsAtTwoHundredAndSkipsKindsAlreadyReturnedByPageGet()
    {
        var fake = new InventoryReader();
        fake.Respond = (tool, args) =>
        {
            if (tool == "eplan_page_get" && Page(args) == SecondCircuitPage)
            {
                return PageResult(SecondCircuitPage, [Drawing("Arc", SecondCircuitPage, 1)]);
            }
            if (tool == "eplan_object_find" && Page(args) == CircuitPage && args.GetProperty("object_type").GetString() == "Arc")
            {
                return Json(new
                {
                    items = Enumerable.Range(args.TryGetProperty("cursor", out _) ? 101 : 1, 100).Select(i => Drawing("Arc", CircuitPage, i)).ToArray(),
                    cursor = args.TryGetProperty("cursor", out _) ? "arcs-200" : "arcs-100",
                    truncated = true,
                });
            }
            return null;
        };
        await fake.RunAsync(TestContext.Current.CancellationToken);

        var arcReads = fake.Calls.Where(c => c.Tool == "eplan_object_find" && Page(c.Args) == CircuitPage && c.Args.GetProperty("object_type").GetString() == "Arc").ToArray();
        Assert.Equal(2, arcReads.Length);
        Assert.Equal(200, arcReads[0].Args.GetProperty("limit").GetInt32());
        Assert.Equal(100, arcReads[1].Args.GetProperty("limit").GetInt32());
        Assert.DoesNotContain(fake.Calls, c => c.Tool == "eplan_object_find" && Page(c.Args) == SecondCircuitPage && c.Args.GetProperty("object_type").GetString() == "Arc");
        Assert.Equal(200, fake.Calls.Count(c => c.Tool == "eplan_object_get"));
        Assert.Equal(200, fake.Saved[^1][2].GetProperty("objectCounts").GetProperty("Arc").GetInt32());
        Assert.Contains(fake.Saved[^1][2].GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("200", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BudgetPassedDuringPage_PersistsCompletedPagesAndGapThenStopsCallingTools()
    {
        var fake = new InventoryReader();
        fake.BeforeCall = (tool, args) =>
        {
            if (tool == "eplan_page_get" && Page(args) == CircuitPage)
            {
                fake.Time.Advance(TimeSpan.FromMinutes(26));
            }
        };
        await fake.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_pageNames.Take(3), fake.Saved[^1].EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(Enumerable.Range(1, 3), fake.Saved.Select(s => s.GetArrayLength()));
        Assert.Equal("eplan_page_get", fake.Calls[^1].Tool);
        Assert.Equal(CircuitPage, Page(fake.Calls[^1].Args));
        Assert.Contains(fake.Saved[^1][2].GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("25", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnreadablePage_RecordsEvidenceGapAndContinuesToLaterPages()
    {
        var fake = new InventoryReader { FailingPage = "&AAB1/1" };
        await fake.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(16, fake.Saved[^1].GetArrayLength());
        Assert.Contains(fake.Saved[^1][1].GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("synthetic unreadable page", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BudgetPassedOnLastReadOfPage_StillPersistsAStopGap()
    {
        var fake = new InventoryReader();
        fake.BeforeCall = (tool, _) =>
        {
            if (tool == "eplan_device_list")
            {
                fake.Time.Advance(TimeSpan.FromMinutes(26));
            }
        };
        await fake.RunAsync(TestContext.Current.CancellationToken);

        Assert.Single(fake.Saved);
        Assert.Equal(1, fake.Saved[0].GetArrayLength());
        Assert.Contains(fake.Saved[0][0].GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("25", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InFlightRead_BudgetCancellation_PersistsCurrentPageAndStopsCleanly()
    {
        var fake = new InventoryReader { HonorCancellation = true };
        fake.BeforeCall = (tool, args) =>
        {
            if (tool == "eplan_page_get" && Page(args) == CircuitPage)
            {
                fake.Time.Advance(TimeSpan.FromMinutes(26));
            }
        };
        await fake.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_pageNames.Take(3), fake.Saved[^1].EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Equal("eplan_page_get", fake.Calls[^1].Tool);
        Assert.Equal(CircuitPage, Page(fake.Calls[^1].Args));
        Assert.Contains(fake.Saved[^1][2].GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("25", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InFlightRead_ExternalCancellation_IsNotConvertedIntoBudgetGap()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fake = new InventoryReader { HonorCancellation = true };
        fake.BeforeCall = (tool, args) =>
        {
            if (tool == "eplan_page_get" && Page(args) == CircuitPage)
            {
                cancellation.Cancel();
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.RunAsync(cancellation.Token));
        Assert.Equal(2, fake.Saved[^1].GetArrayLength());
        Assert.Equal(CircuitPage, Page(fake.Calls[^1].Args));
    }

    private static string Page(JsonElement args) => args.GetProperty("page").GetString()!;
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    private static JsonElement Drawing(string kind, string page, int oid, Dictionary<string, string>? properties = null) => Json(new
    {
        kind,
        locator = new { project = Project, page, type = kind == "Function" ? 17 : 21, oid, digest = "synthetic" },
        properties = properties ?? [],
    });
    private static JsonElement PageResult(string page, JsonElement[] items, string? cursor = null) => Json(new
    {
        page = new { name = page, type = SmallProjectInventory.Pages.Single(p => p.Name == page).Type },
        properties = new[] { new { id = 11015, value = "SyntheticForm" } },
        drawings = new { items, nextCursor = cursor, truncated = cursor is not null },
        truncated = false,
    });

    private sealed class InventoryReader
    {
        public ManualTime Time { get; } = new(DateTimeOffset.UnixEpoch);
        public List<(string Tool, JsonElement Args)> Calls { get; } = [];
        public List<JsonElement> Saved { get; } = [];
        public List<string> Log { get; } = [];
        public Func<string, JsonElement, JsonElement?>? Respond { get; set; }
        public Action<string, JsonElement>? BeforeCall { get; set; }
        public string? FailingPage { get; init; }
        public bool HonorCancellation { get; init; }

        public Task RunAsync(CancellationToken cancellationToken = default) => new SmallProjectInventory(CallAsync, Time, Log.Add).RunAsync(Project, pages =>
        {
            Saved.Add(Json(pages));
            return Task.CompletedTask;
        }, cancellationToken);

        private Task<(bool Succeeded, JsonElement Result)> CallAsync(string tool, Dictionary<string, object?> args, CancellationToken cancellationToken)
        {
            JsonElement input = Json(args);
            Calls.Add((tool, input));
            BeforeCall?.Invoke(tool, input);
            if (HonorCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (tool == "eplan_page_get" && Page(input) == FailingPage)
            {
                return Task.FromResult((false, Json(new { reason = "EPLAN_OPERATION_FAILED", message = "synthetic unreadable page" })));
            }
            JsonElement output = Respond?.Invoke(tool, input) ?? DefaultResponse(tool, input);
            return Task.FromResult((true, output));
        }

        private static JsonElement DefaultResponse(string tool, JsonElement input) => tool switch
        {
            "eplan_page_get" => PageResult(Page(input), []),
            "eplan_device_list" => Json(new { devices = System.Array.Empty<object>(), nextCursor = (string?)null, truncated = false }),
            "eplan_object_find" or "eplan_object_get" => Json(new { items = System.Array.Empty<object>(), cursor = (string?)null, truncated = false }),
            "eplan_property_get" => PropertyResult(input),
            "eplan_symbol_search" => Json(new
            {
                symbols = new[] { new { library = "IEC_symbol", name = "K", variant = 0, functionCategory = "Coil" }, new { library = "IEC_symbol", name = "K", variant = 1, functionCategory = "Coil" } },
                nextCursor = (string?)null,
                truncated = false,
            }),
            "eplan_device_get" => Json(new
            {
                device = new { tag = input.GetProperty("tag").GetString(), page = CircuitPage, category = "Coil" },
                parts = new[] { new { partNumber = "SyntheticPart", count = 1 } },
                properties = System.Array.Empty<object>(),
                truncated = false,
            }),
            _ => throw new InvalidOperationException("Unexpected tool: " + tool),
        };

        private static JsonElement PropertyResult(JsonElement input) => Json(new
        {
            items = input.GetProperty("queries").EnumerateArray().SelectMany((query, index) => query.GetProperty("properties").EnumerateArray().Select(property => new
            {
                query = index,
                target = query.GetProperty("target"),
                property_id = property.GetProperty("property_id").GetInt32(),
                value = property.GetProperty("property_id").GetInt32() switch
                {
                    20111 => "IEC_symbol",
                    20112 => "K",
                    20113 => (query.GetProperty("target").GetProperty("locator").GetProperty("oid").GetInt32() % 2).ToString(CultureInfo.InvariantCulture),
                    _ => throw new InvalidOperationException("Only symbol identity properties may be requested."),
                },
                status = "value",
            })).ToArray(),
            cursor = (string?)null,
            truncated = false,
        });
    }
}

using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S9;

/// <summary>
/// Spike S9 (write API, M3 design 4.5). Answers what the first write tools need before they are built: how a full page
/// name maps to the name parts of a new page, what renaming a device does to its auxiliary functions, how a window macro
/// is inserted and what it creates, whether generating connections makes the drawn ones real, and which of SafetyPoint
/// and Transaction can wrap (and undo) each of these.
/// <para>
/// Usage: <c>EplanMcpSpikeS9 /Mode:pages|rename|place|placeauto|macro|connect|symbols|close /Report:"…\x.json"
/// /ScratchRoot:"…\scratch" [/Macro:"…\x.ema"]</c>. Works only on an open project under ScratchRoot, found by path; a
/// change is made in its own SafetyPoint and committed on that scratch copy, except the probes that roll back on purpose
/// and the placement probes that try a Transaction or no wrapper instead.
/// </para>
/// </summary>
public sealed class SpikeS9Action : IEplAction
{
    // A page the macro and connection probes work on; "pages" creates it.
    private static readonly (string Plant, string Location, string DocType, int Counter) _probePage = ("GAA", "A1", "EFS1", 50);

    private static string _reportPath = string.Empty;

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        string assemblyName = typeof(SpikeS9Action).Assembly.GetName().Name ?? "EplanMcp.Spikes.S9";
        Name = assemblyName.Replace("EplanMcp.Spikes.", "EplanMcpSpike").Replace(".", string.Empty);
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        _reportPath = Parameter(oActionCallingContext, "Report");
        string scratchRoot = Parameter(oActionCallingContext, "ScratchRoot");
        string mode = Parameter(oActionCallingContext, "Mode");
        var report = new JObject
        {
            ["spike"] = "S9-write-api",
            ["mode"] = mode,
            ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
        };

        try
        {
            using (new QuietModeStep(QuietModes.ShowNoDialogs))
            {
                // "placeauto": no LockingStep of our own, so the action's implicit, automatic locking applies (r6:
                // the new function itself could not be locked under an explicit LockingStep).
                if (mode == "placeauto")
                {
                    Run(report, scratchRoot, "place", Parameter(oActionCallingContext, "Macro"));
                }
                else
                {
                    using (new LockingStep())
                    {
                        Run(report, scratchRoot, mode, Parameter(oActionCallingContext, "Macro"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            report["fatal"] = Describe(ex);
        }

        Save(report);
        return true;
    }

    private static void Run(JObject report, string scratchRoot, string mode, string macroPath)
    {
        Project? project = OpenProjects().FirstOrDefault(p => IsUnder(p.ProjectFullName, scratchRoot));
        if (project is null)
        {
            report["refused"] = "no open project under the scratch root";
            return;
        }

        report["project"] = project.ProjectFullName;
        switch (mode)
        {
            case "pages":
                Pages(report, project);
                break;
            case "rename":
                Rename(report, project);
                break;
            case "place":
                Place(report, project);
                break;
            case "macro":
                Macro(report, project, macroPath);
                break;
            case "connect":
                Connect(report, project);
                break;
            case "symbols":
                Probe(report, "symbol_libraries", () => new JArray(project.SymbolLibraries.Select(l => l.Name)));
                Probe(report, "iec_symbols", () => Symbols(project, "IEC_symbol"));
                break;
            case "close":
                Probe(report, "close", () =>
                {
                    project.Close();
                    return "closed";
                });
                break;
            default:
                report["refused"] = "unknown mode " + mode;
                break;
        }
    }

    private static void Pages(JObject report, Project project)
    {
        // Which name part is which prefix: the sample's pages are named like =GAA+A1&EFS1/1.
        Probe(report, "create_plant_location_doctype", () => Committed(() => Describe(NewPage(project, p =>
        {
            p.DESIGNATION_PLANT = _probePage.Plant;
            p.DESIGNATION_LOCATION = _probePage.Location;
            p.DESIGNATION_DOCTYPE = _probePage.DocType;
            p.PAGE_COUNTER = _probePage.Counter;
        }))));
        Probe(report, "create_functionalassignment_instead", () => Committed(() => Describe(NewPage(project, p =>
        {
            p.DESIGNATION_FUNCTIONALASSIGNMENT = "GAA";
            p.DESIGNATION_LOCATION = "A1";
            p.DESIGNATION_DOCTYPE = "EFS1";
            p.PAGE_COUNTER = 51;
        }))));
        Probe(report, "create_installationnumber_instead", () => Committed(() => Describe(NewPage(project, p =>
        {
            p.DESIGNATION_INSTALLATIONNUMBER = "GAA";
            p.DESIGNATION_LOCATION = "A1";
            p.DESIGNATION_DOCTYPE = "EFS1";
            p.PAGE_COUNTER = 52;
        }))));

        // A second page with an existing name: refused, renamed, or a duplicate? Rolled back either way.
        Probe(report, "create_duplicate_name", () =>
        {
            using SafetyPoint safety = SafetyPoint.Create();
            Page page = NewPage(project, p =>
            {
                p.DESIGNATION_PLANT = _probePage.Plant;
                p.DESIGNATION_LOCATION = _probePage.Location;
                p.DESIGNATION_DOCTYPE = _probePage.DocType;
                p.PAGE_COUNTER = _probePage.Counter;
            });
            JObject result = Describe(page);
            result["pagesWithThatName"] = PagesNamed(project, page.Name);
            safety.Rollback();
            return result;
        });

        // Page.Name is settable and "not checked whether the given name already exists" (XML docs).
        Probe(report, "set_name_directly", () => Committed(() =>
        {
            Page page = NewPage(project, p =>
            {
                p.DESIGNATION_LOCATION = "A1";
                p.PAGE_COUNTER = 53;
            });
            string before = page.Name;
            page.Name = "=GAA+A1&EFS1/54";
            JObject result = Describe(page);
            result["before"] = before;
            return result;
        }));

        Probe(report, "set_description_zh", () => Committed(() =>
        {
            Page page = PageNamed(project, ProbePageName()) ?? throw new InvalidOperationException("probe page missing");
            using var text = new MultiLangString();
            text.AddString(ISOCode.Language.L_zh_CN, "S9 测试页");
            page.Properties.PAGE_NOMINATIOMN = text;
            using MultiLangString read = page.Properties.PAGE_NOMINATIOMN.ToMultiLangString();
            return new JObject { ["zh_CN"] = read.GetStringToDisplay(ISOCode.Language.L_zh_CN), ["raw"] = page.Properties.PAGE_NOMINATIOMN.ToString() };
        }));
    }

    private static void Rename(JObject report, Project project)
    {
        Function[] devices = Functions(project, null).Where(f => f.IsMainFunction && f.Page is not null && f.SubFunctions.Length > 0 && !string.IsNullOrEmpty(f.Name)).ToArray();
        report["devicesWithAuxiliaries"] = devices.Length;
        Function? device = devices.FirstOrDefault(f => f.FunctionCategory.ToString().Contains("Coil")) ?? devices.FirstOrDefault();
        if (device is null)
        {
            report["refused"] = "no device with auxiliary functions";
            return;
        }

        Probe(report, "rename_with_auxiliaries", () => Committed(() =>
        {
            JObject before = DescribeDevice(device);
            device.Name += "9";
            return new JObject { ["before"] = before, ["after"] = DescribeDevice(device), ["namedAfter"] = FunctionsNamed(project, device.Name) };
        }));

        Function? whole = devices.FirstOrDefault(f => f != device && f.Name != device.Name && f.SubFunctions.Length > 0);
        Probe(report, "rename_whole_device", () =>
        {
            if (whole is null)
            {
                return "no second device";
            }

            return Committed(() =>
            {
                JObject before = DescribeDevice(whole);
                Function[] auxiliaries = whole.SubFunctions;
                string renamed = whole.Name + "8";
                whole.Name = renamed;
                foreach (Function auxiliary in auxiliaries)
                {
                    auxiliary.Name = renamed;
                }

                return new JObject
                {
                    ["before"] = before,
                    ["after"] = DescribeDevice(whole),
                    ["auxiliariesAfter"] = new JArray(auxiliaries.Select(f => new JObject { ["name"] = f.Name, ["visibleName"] = f.VisibleName, ["main"] = f.IsMainFunction })),
                    ["namedAfter"] = FunctionsNamed(project, renamed),
                };
            });
        });

        Function? other = devices.FirstOrDefault(f => f != device && f != whole && f.Name != device.Name);
        Probe(report, "rename_to_an_existing_tag", () =>
        {
            if (other is null)
            {
                return "no second device";
            }

            using SafetyPoint safety = SafetyPoint.Create();
            string taken = device.Name;
            JObject before = DescribeDevice(other);
            other.Name = taken;
            var result = new JObject { ["before"] = before, ["after"] = DescribeDevice(other), ["functionsNamedTaken"] = FunctionsNamed(project, taken) };
            safety.Rollback();
            result["afterRollback"] = DescribeDevice(other);
            return result;
        });
    }

    private static void Place(JObject report, Project project)
    {
        Page page = PageNamed(project, ProbePageName()) ?? throw new InvalidOperationException("probe page missing; run 'pages' first");
        Probe(report, "lock_page", () =>
        {
            var result = new JObject { ["isLockedBefore"] = page.IsLocked };
            result["lockObject"] = Try(() =>
            {
                page.LockObject();
                return "locked";
            });
            result["smartLock"] = Try(() =>
            {
                page.SmartLock();
                return "locked";
            });
            result["isLockedAfter"] = page.IsLocked;
            return result;
        });
        // r9: a script once created a function this way (api-notes A3) without a SafetyPoint; every r2–r8 attempt was in one.
        Probe(report, "place_coil_without_safety_point", () => PlaceCoil(project, page, "=GAA+A1-KF94", 60, 160));
        Probe(report, "place_coil_in_transaction", () =>
        {
            using Transaction transaction = new TransactionManager().CreateTransaction();
            JObject placed = PlaceCoil(project, page, "=GAA+A1-KF95", 60, 120);
            transaction.Commit();
            return placed;
        });
        Probe(report, "place_coil_undo_step_in_safety_point", () => Committed(() =>
        {
            using var undoManager = new UndoManager();
            UndoStep undo = undoManager.CreateUndoStep();
            JObject placed = PlaceCoil(project, page, "=GAA+A1-KF96", 60, 80);
            undo.SetUndoDescription("S9 place coil");
            undo.CloseUndo();
            return placed;
        }));
        Probe(report, "functions_after_r9_probes", () => DescribeFunctions(project, page));

        // r10: if a Transaction is to replace the SafetyPoint around writes, giving it up must take the new function away.
        // r11: an unfiltered GetFunctions did not see the new function inside the transaction (r10); count by page too.
        Probe(report, "place_coil_transaction_abort", () =>
        {
            int before = Functions(project, page).Length;
            using Transaction transaction = new TransactionManager().CreateTransaction();
            JObject placed = PlaceCoil(project, page, "=GAA+A1-KF97", 100, 160);
            placed["namedBeforeAbort"] = FunctionsNamed(project, "=GAA+A1-KF97");
            placed["onPageBefore"] = before;
            placed["onPageBeforeAbort"] = Functions(project, page).Length;
            transaction.Abort();
            placed["namedAfterAbort"] = FunctionsNamed(project, "=GAA+A1-KF97");
            placed["onPageAfterAbort"] = Functions(project, page).Length;
            return placed;
        });
        Probe(report, "place_coil_transaction_not_committed", () =>
        {
            using (new TransactionManager().CreateTransaction())
            {
                PlaceCoil(project, page, "=GAA+A1-KF98", 100, 120);
            }

            return new JObject { ["namedAfterDispose"] = FunctionsNamed(project, "=GAA+A1-KF98") };
        });
        Probe(report, "place_coil_undo_step_around_transaction", () =>
        {
            using var undoManager = new UndoManager();
            UndoStep undo = undoManager.CreateUndoStep();
            using Transaction transaction = new TransactionManager().CreateTransaction();
            JObject placed = PlaceCoil(project, page, "=GAA+A1-KF99", 100, 80);
            transaction.Commit();
            undo.SetUndoDescription("S9 place coil");
            undo.CloseUndo();
            placed["namedAfterCommit"] = FunctionsNamed(project, "=GAA+A1-KF99");
            return placed;
        });
        Probe(report, "place_same_tag_twice", () => new JObject { ["named"] = FunctionsNamed(project, "=GAA+A1-KF94") });

        // r12: a macro insertion is undone by a SafetyPoint only, a function is created in a Transaction only (r10, r11).
        // Does a Transaction inside a SafetyPoint give both?
        Probe(report, "place_coil_transaction_in_safety_point_rollback", () =>
        {
            int before = Functions(project, page).Length;
            var result = new JObject();
            using (SafetyPoint safety = SafetyPoint.Create())
            {
                using (Transaction transaction = new TransactionManager().CreateTransaction())
                {
                    result["placed"] = PlaceCoil(project, page, "=GAA+A1-KF80", 140, 160);
                    transaction.Commit();
                }

                result["onPageDuring"] = Functions(project, page).Length;
                safety.Rollback();
            }

            result["onPageBefore"] = before;
            result["onPageAfterRollback"] = Functions(project, page).Length;
            result["namedAfterRollback"] = FunctionsNamed(project, "=GAA+A1-KF80");
            return result;
        });
        Probe(report, "place_coil_transaction_in_safety_point_commit", () => Committed(() =>
        {
            using Transaction transaction = new TransactionManager().CreateTransaction();
            JObject placed = PlaceCoil(project, page, "=GAA+A1-KF81", 140, 120);
            transaction.Commit();
            return placed;
        }));
        Probe(report, "named_after_commit", () => new JObject { ["KF81"] = FunctionsNamed(project, "=GAA+A1-KF81") });
        Probe(report, "place_coil_symbol", () => Committed(() =>
        {
            return new JArray(PlaceCoil(project, page, "=GAA+A1-KF90", 40, 160), PlaceCoil(project, page, "=GAA+A1-KF91", 40, 120));
        }));

        // r7: the 2-argument Create fails to lock the new object with or without our LockingStep; try the overload that
        // also takes the logical area.
        Probe(report, "place_coil_with_area", () => Committed(() =>
        {
            using var library = new SymbolLibrary(project, "IEC_symbol");
            using var symbol = new Symbol(library, "K");
            var function = new Function();
            function.Create(page, symbol.Variants[0], new PointD(30, 30), new PointD(50, 50));
            function.Name = "=GAA+A1-KF93";
            return new JObject { ["name"] = function.Name, ["visibleName"] = function.VisibleName, ["pins"] = new JArray(function.Pins.Select(p => p.Name)) };
        }));

        // Control: if only a lock is missing, locking everything first (never done in the product) lets it through.
        Probe(report, "place_coil_after_lock_all", () => Committed(() =>
        {
            project.LockAllObjects();
            return new JArray(PlaceCoil(project, page, "=GAA+A1-KF92", 40, 80));
        }));
    }

    private static StorableObject[] InsertMacro(Project project, Page page, string macroPath, double x, double y)
    {
        using var macro = new WindowMacro();
        macro.Open(macroPath, project);
        using var insert = new Insert();
        return insert.WindowMacro(macro, WindowMacro.Enums.RepresentationType.MultiLine, 0, page, new PointD(x, y), Insert.MoveKind.Absolute);
    }

    private static JObject PlaceCoil(Project project, Page page, string name, double x, double y)
    {
        using var library = new SymbolLibrary(project, "IEC_symbol");
        using var symbol = new Symbol(library, "K");
        SymbolVariant variant = symbol.Variants[0];
        var function = new Function();
        function.Create(page, variant);
        function.Location = new PointD(x, y);
        string unnamed = function.Name;
        function.Name = name;
        return new JObject
        {
            ["nameBeforeNaming"] = unnamed,
            ["name"] = function.Name,
            ["visibleName"] = function.VisibleName,
            ["category"] = function.FunctionCategory.ToString(),
            ["page"] = function.Page?.Name,
            ["location"] = function.Location.X.ToString(CultureInfo.InvariantCulture) + "," + function.Location.Y.ToString(CultureInfo.InvariantCulture),
            ["pins"] = new JArray(function.Pins.Select(p => p.Name)),
            ["isMain"] = function.IsMainFunction,
        };
    }

    private static void Macro(JObject report, Project project, string macroPath)
    {
        report["macroPath"] = macroPath;
        report["macroExists"] = File.Exists(macroPath);
        Page page = PageNamed(project, ProbePageName()) ?? throw new InvalidOperationException("probe page missing; run 'pages' first");
        report["page"] = page.Name;
        Probe(report, "functions_before", () => DescribeFunctions(project, page));
        Probe(report, "macro_representations", () =>
        {
            using var macro = new WindowMacro();
            macro.Open(macroPath, project);
            return new JObject
            {
                ["current"] = macro.RepresentationType.ToString(),
                ["all"] = new JArray(macro.RepresentationTypes.Select(t => t.ToString())),
            };
        });

        // r2 inserted variant 0, an ArticlePlacement representation; a circuit page wants the multi-line one.
        // r10: the same insertion in a Transaction that is given up stayed on the page; r11: and in a SafetyPoint that is
        // not committed? Each at its own point: an identical symbol on top of another is refused (r10).
        Probe(report, "insert_window_macro_transaction_abort", () =>
        {
            int before = Functions(project, page).Length;
            using Transaction transaction = new TransactionManager().CreateTransaction();
            StorableObject[] placed = InsertMacro(project, page, macroPath, 200, 240);
            int during = Functions(project, page).Length;
            transaction.Abort();
            return new JObject { ["functionsBefore"] = before, ["placed"] = placed.Length, ["functionsDuring"] = during, ["functionsAfterAbort"] = Functions(project, page).Length, ["placedValidAfterAbort"] = placed.Count(o => o.IsValid) };
        });
        Probe(report, "insert_window_macro_safety_point_rollback", () =>
        {
            int before = Functions(project, page).Length;
            StorableObject[] placed;
            int during;
            using (SafetyPoint safety = SafetyPoint.Create())
            {
                placed = InsertMacro(project, page, macroPath, 200, 220);
                during = Functions(project, page).Length;
                safety.Rollback();
            }

            return new JObject { ["functionsBefore"] = before, ["placed"] = placed.Length, ["functionsDuring"] = during, ["functionsAfterRollback"] = Functions(project, page).Length, ["placedValidAfterRollback"] = placed.Count(o => o.IsValid) };
        });
        Probe(report, "insert_window_macro", () => Committed(() =>
        {
            using var macro = new WindowMacro();
            macro.Open(macroPath, project);
            WindowMacro.Enums.RepresentationType representation = macro.RepresentationTypes.Contains(WindowMacro.Enums.RepresentationType.MultiLine)
                ? WindowMacro.Enums.RepresentationType.MultiLine
                : macro.RepresentationType;
            using var insert = new Insert();
            StorableObject[] placed = insert.WindowMacro(macro, representation, 0, page, new PointD(200, 200), Insert.MoveKind.Absolute);
            return new JObject
            {
                ["placed"] = placed.Length,
                ["types"] = new JArray(placed.Select(p => p.GetType().Name).Distinct()),
                ["functions"] = DescribeFunctions(project, page),
            };
        }));
    }

    private static void Connect(JObject report, Project project)
    {
        Page page = PageNamed(project, ProbePageName()) ?? throw new InvalidOperationException("probe page missing; run 'pages' first");
        Probe(report, "connections_before", () => new JObject { ["page"] = DescribeConnections(project, page), ["project"] = AllConnections(project) });
        Probe(report, "generate_connections_page", () => Committed(() =>
        {
            using var generate = new Generate();
            generate.Connections([page], false);
            return new JObject { ["page"] = DescribeConnections(project, page), ["project"] = AllConnections(project) };
        }));
    }

    private static JArray Symbols(Project project, string library)
    {
        using var symbols = new SymbolLibrary(project, library);
        return new JArray(symbols.Symbols.Take(40).Select(s => new JObject { ["name"] = s.Name, ["variants"] = s.Variants.Length }));
    }

    // --- helpers ---

    private static string ProbePageName() =>
        string.Format(CultureInfo.InvariantCulture, "={0}+{1}&{2}/{3}", _probePage.Plant, _probePage.Location, _probePage.DocType, _probePage.Counter);

    private static Page NewPage(Project project, Action<PagePropertyList> parts)
    {
        using var properties = new PagePropertyList();
        parts(properties);
        var page = new Page();
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, properties);
        return page;
    }

    private static JToken Committed(Func<JToken> change)
    {
        using SafetyPoint safety = SafetyPoint.Create();
        JToken result = change();
        safety.Commit();
        return result;
    }

    private static Page? PageNamed(Project project, string name)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).FirstOrDefault(p => p.Name == name);
    }

    private static int PagesNamed(Project project, string name)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).Count(p => p.Name == name);
    }

    private static Function[] Functions(Project project, Page? page)
    {
        using var finder = new DMObjectsFinder(project);
        if (page is null)
        {
            return finder.GetFunctions(null);
        }

        using var filter = new FunctionsFilter { Page = page };
        return finder.GetFunctions(filter);
    }

    private static int FunctionsNamed(Project project, string name) => Functions(project, null).Count(f => f.Name == name);

    private static int ConnectionsOn(Project project, Page page)
    {
        using var finder = new DMObjectsFinder(project);
        // 3D connections have no readable pins (S6; first S9 run threw on them).
        return finder.GetConnections(null).Where(c => c is not Connection3D).Count(c => c.StartPin?.ParentFunction?.Page == page || c.EndPin?.ParentFunction?.Page == page);
    }

    private static JArray DescribeConnections(Project project, Page page)
    {
        using var finder = new DMObjectsFinder(project);
        return new JArray(finder.GetConnections(null).Where(c => c is not Connection3D)
            .Where(c => c.StartPin?.ParentFunction?.Page == page || c.EndPin?.ParentFunction?.Page == page)
            .Select(c => c.StartPin?.ParentFunction?.Name + ":" + c.StartPin?.Name + " - " + c.EndPin?.ParentFunction?.Name + ":" + c.EndPin?.Name));
    }

    private static int AllConnections(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetConnections(null).Length;
    }

    private static JObject Describe(Page page) => new()
    {
        ["name"] = page.Name,
        ["plant"] = Text(page.Properties.DESIGNATION_PLANT),
        ["functionalAssignment"] = Text(page.Properties.DESIGNATION_FUNCTIONALASSIGNMENT),
        ["installationNumber"] = Text(page.Properties.DESIGNATION_INSTALLATIONNUMBER),
        ["location"] = Text(page.Properties.DESIGNATION_LOCATION),
        ["docType"] = Text(page.Properties.DESIGNATION_DOCTYPE),
        ["counter"] = Text(page.Properties.PAGE_COUNTER),
    };

    private static JObject DescribeDevice(Function device) => new()
    {
        ["name"] = device.Name,
        ["visibleName"] = device.VisibleName,
        ["page"] = device.Page?.Name,
        ["auxiliaries"] = new JArray(device.SubFunctions.Select(f => new JObject { ["name"] = f.Name, ["visibleName"] = f.VisibleName, ["page"] = f.Page?.Name })),
    };

    private static JArray DescribeFunctions(Project project, Page page) =>
        new(Functions(project, page).Select(f => new JObject
        {
            ["name"] = f.Name,
            ["visibleName"] = f.VisibleName,
            ["main"] = f.IsMainFunction,
            ["category"] = f.FunctionCategory.ToString(),
            ["pins"] = f.Pins.Length,
        }));

    private static string? Text(PropertyValue value) => value.IsEmpty ? null : value.ToString();

    private static void Probe(JObject report, string name, Func<JToken> probe)
    {
        report["inProgress"] = name;
        Save(report);
        try
        {
            report[name] = probe();
        }
        catch (Exception ex)
        {
            report[name] = new JObject { ["error"] = Describe(ex) };
        }

        report.Remove("inProgress");
    }

    private static Project[] OpenProjects()
    {
        using var manager = new ProjectManager();
        bool lockByDefault = manager.LockProjectByDefault;
        manager.LockProjectByDefault = false;
        try
        {
            return manager.OpenProjects;
        }
        finally
        {
            manager.LockProjectByDefault = lockByDefault;
        }
    }

    private static bool IsUnder(string? path, string root) =>
        path is not null && root.Length > 0
        && Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static JObject DescribeObject(StorableObject? found) => new()
    {
        ["type"] = found?.GetType().FullName,
        ["name"] = found switch
        {
            FunctionBase function => function.Name,
            Page page => page.Name,
            Project project => project.ProjectName,
            _ => found?.ToString(),
        },
    };

    private static JToken Try(Func<JToken> probe)
    {
        try
        {
            return probe();
        }
        catch (Exception ex)
        {
            return Describe(ex);
        }
    }

    private static JObject Describe(Exception ex)
    {
        var o = new JObject { ["type"] = ex.GetType().FullName, ["message"] = ex.Message };
        if (ex is LockingExceptionFailedLockAttempt locking)
        {
            o["lockingStep"] = "see mode";
            o["failedToLock"] = new JArray(locking.GetAllFailed2LockAsString().Select(id => new JObject { ["id"] = id, ["object"] = Try(() => DescribeObject(StorableObject.FromStringIdentifier(id))) }));
        }

        if (ex.InnerException is not null)
        {
            o["inner"] = Describe(ex.InnerException);
        }

        return o;
    }

    private static void Save(JObject report)
    {
        if (_reportPath.Length > 0)
        {
            File.WriteAllText(_reportPath, report.ToString(Formatting.Indented));
        }
    }

    private static string Parameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        return value ?? string.Empty;
    }
}

using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using Newtonsoft.Json.Linq;
using EplanInsert = Eplan.EplApi.HEServices.Insert;

namespace EplanMcp.Spikes.S36;

/// <summary>Fixed sample and explicit bindings only; the complete native reference closure belongs to S36 part 2.</summary>
internal sealed class Probe(Project project) : IDisposable
{
    private const int SentinelCounter = 936;
    private const int ModuleCounter = 937;
    private readonly Dictionary<(int Type, long Oid), UniversalPropertyList> _lists = [];
    private static readonly (string Variable, int Id, int Index)[] _functionBindings =
    [
        ("Boolean", 20093, 0), // FUNC_SUPPRESSINDEVICELIST: boolean, not the ApplyRecord overload flag.
        ("Multilingual", 20011, 0), // FUNC_TEXT.
        ("Part", 20100, 1), // FUNC_ARTICLE_PARTNR[1].
        ("Plain", 20027, 0), // FUNC_TECHNICAL_CHARACTERISTIC: string.
    ];

    public void Sentinel()
    {
        if (Find(SentinelCounter) is not null || Find(ModuleCounter) is not null)
        {
            throw new InvalidOperationException("Reserved page counter 936 or 937 exists; undo this probe or reset the closed fixture.");
        }

#pragma warning disable CA2000 // Borrowed persistent page (A39).
        Page page = NewPage(SentinelCounter);
#pragma warning restore CA2000
        PagePropertyList properties = (PagePropertyList)Properties(page);
        using MultiLangString description = Text("S36 sentinel");
        properties.PAGE_NOMINATIOMN = description;
    }

    public PlaceHolder Insert(Action<string, object> log)
    {
        Page sentinel = Find(SentinelCounter) ?? throw new InvalidOperationException("Run sentinel first.");
        PagePropertyList sentinelProperties = (PagePropertyList)Properties(sentinel);
        using (PropertyValue description = sentinelProperties.PAGE_NOMINATIOMN)
        {
            if (description.IsEmpty)
            {
                throw new InvalidOperationException("Page 936 is not the S36 sentinel.");
            }

            using MultiLangString text = description.ToMultiLangString();
            if (!string.Equals(text.GetStringToDisplay(ISOCode.Language.L_en_US), "S36 sentinel", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Page 936 is not the unchanged S36 sentinel.");
            }
        }

        if (Find(ModuleCounter) is not null)
        {
            throw new InvalidOperationException("Module page 937 exists; do not apply twice.");
        }

#pragma warning disable CA2000 // Borrowed persistent page (A39).
        Page page = NewPage(ModuleCounter);
#pragma warning restore CA2000
        using var source = new WindowMacro();
        source.Open(PathMap.SubstitutePath(@"$(MD_MACROS)\EPLAN_Macro\202_Power_Supply_24V\101_01_Variant_1\24VDC_Supply.ema"), project);
        using var insert = new EplanInsert();
        StorableObject[] inserted = insert.WindowMacro(source, WindowMacro.Enums.RepresentationType.MultiLine, 1,
            page, new PointD(100, 150), EplanInsert.MoveKind.Absolute, WindowMacro.Enums.NumerationMode.Number);
        Function target = page.Functions.Where(f => f.IsMainFunction).OrderBy(f => f.ObjectIdentifier).First();
        FunctionPropertyList properties = (FunctionPropertyList)Properties(target);
        properties.FUNC_SUPPRESSINDEVICELIST = false;
        using MultiLangString initial = Text("S36 English before", ISOCode.Language.L_en_US);
        initial.AddString(ISOCode.Language.L_de_DE, "S36 Deutsch behalten");
        properties.FUNC_TEXT = initial;
        properties.FUNC_TECHNICAL_CHARACTERISTIC = "S36 plain before";
        using PropertyValue originalPart = properties.FUNC_ARTICLE_PARTNR[1];
        string partBefore = originalPart.IsEmpty ? string.Empty : originalPart.ToString();
        string part = project.Articles.Select(a => a.PartNr).Where(p => !string.IsNullOrEmpty(p) && !string.Equals(p, partBefore, StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal).First(); // Only a part already in the shipped sample project; no master-data write.
#pragma warning disable CA2000 // Persistent placement: never Dispose a created/returned StorableObject (A39).
        var holder = new PlaceHolder();
#pragma warning restore CA2000
        holder.Create(page);
        holder.Name = "S36";
        foreach ((string variable, int id, int index) in _functionBindings)
        {
            using AnyPropertyId property = Property(id, index);
            Bind(holder, target, property, variable);
        }

        using AnyPropertyId pageDescription = Eplan.EplApi.DataModel.Properties.Page.PAGE_NOMINATIOMN;
        Bind(holder, sentinel, pageDescription, "Page");
        using AnyPropertyId projectField = Property(10901, 1000);
        holder.AddVariable("Project");
        using MultiLangString projectEntry = Text("<Project>");
        holder.SetProjectPropertyEntry(projectField, projectEntry);
        holder.AddRecord("Apply");
        Set(holder, "Boolean", "1");
        Set(holder, "Multilingual", "S36 English after", ISOCode.Language.L_en_US);
        Set(holder, "Part", part);
        Set(holder, "Plain", "S36 plain after");
        Set(holder, "Page", "S36 page applied");
        Set(holder, "Project", "S36 project applied");
        log("fixture", new
        {
            macro = "24VDC_Supply.ema",
            representation = "MultiLine",
            macroVariant = 1,
            numbering = "Number",
            inserted = inserted.Length,
            target = target.ObjectIdentifier,
            partBefore,
            requestedPart = part
        });
        return holder;
    }

    public JObject Read()
    {
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
        using var finder = new DMObjectsFinder(project);
        Page[] pages = finder.GetPages(null);
        var result = new JObject { ["pages"] = pages.Length, ["936"] = null, ["937"] = null };
        ProjectPropertyList projectProperties = (ProjectPropertyList)Properties(project);
        result["projectField"] = ReadProperty(project, projectProperties, "Project", 10901, 1000);
        var seen = new HashSet<int>();
        foreach (Page page in pages)
        {
            PagePropertyList pageProperties = (PagePropertyList)Properties(page);
            int counter = Counter(pageProperties);
            if (counter is not (SentinelCounter or ModuleCounter))
            {
                continue;
            }

            if (!seen.Add(counter))
            {
                throw new InvalidOperationException("Ambiguous reserved page counter.");
            }

            var state = new JObject
            {
                ["oid"] = page.ObjectIdentifier,
                ["name"] = page.Name,
                ["description"] = ReadValue(page, "Page", 11011, 0, () => pageProperties.PAGE_NOMINATIOMN),
                ["objects"] = new JArray(page.AllPlacements.OrderBy(p => p.ObjectIdentifier).Select(p =>
                    new JObject { ["type"] = p.GetType().Name, ["oid"] = p.ObjectIdentifier })),
            };
            var functions = new JArray();
            foreach (Function function in page.Functions.OrderBy(f => f.ObjectIdentifier))
            {
                FunctionPropertyList functionProperties = (FunctionPropertyList)Properties(function);
                var values = new JArray(_functionBindings.Select(binding =>
                    ReadProperty(function, functionProperties, binding.Variable, binding.Id, binding.Index)));
                functions.Add(new JObject
                {
                    ["oid"] = function.ObjectIdentifier,
                    ["name"] = function.Name,
                    ["main"] = function.IsMainFunction,
                    ["properties"] = values,
                    ["articleReferences"] = new JArray(function.ArticleReferences.Select(r =>
                        new JObject { ["part"] = r.PartNr, ["variant"] = r.VariantNr, ["count"] = r.Count })),
                });
            }

            state["functions"] = functions;
            state["holders"] = new JArray(page.AllPlacements.OfType<PlaceHolder>().OrderBy(h => h.ObjectIdentifier).Select(holder => new JObject
            {
                ["oid"] = holder.ObjectIdentifier,
                ["name"] = holder.Name,
                ["references"] = holder.NumberOfReferences,
                ["assigned"] = new JArray(holder.AssignedObjects.OrderBy(o => o.ObjectIdentifier).Select(o => new JObject { ["type"] = o.GetType().Name, ["oid"] = o.ObjectIdentifier })),
                ["values"] = new JArray(Enumerable.Range(0, holder.NumberOfRecords).SelectMany(i =>
                    holder.VariableNames.Cast<string>().OrderBy(v => v, StringComparer.Ordinal).Select(variable => new JObject
                    {
                        ["record"] = holder.get_NameOfRecord(i),
                        ["variable"] = variable,
                        ["raw"] = holder.get_Value(holder.get_NameOfRecord(i), variable)
                    }))),
            }));
            result[counter.ToString(CultureInfo.InvariantCulture)] = state;
        }

        return result;
    }

    private static JObject ReadProperty(StorableObject target, UniversalPropertyList properties, string variable, int id, int index)
    {
        // Like EplanPropertyReadAdapter: the id is only the number; the index belongs to the list accessor.
        using AnyPropertyId property = id;
        return ReadValue(target, variable, id, index, () => index == 0 ? properties[property] : properties[property, index]);
    }

    private static JObject ReadValue(StorableObject target, string variable, int id, int index, Func<PropertyValue> read)
    {
        var result = new JObject
        {
            ["objectType"] = target.GetType().Name,
            ["oid"] = target.ObjectIdentifier,
            ["variable"] = variable,
            ["propertyId"] = id,
            ["index"] = index,
        };
        try
        {
            using PropertyValue value = read();
            result["empty"] = value.IsEmpty;
            if (!value.IsEmpty)
            {
                result["raw"] = value.ToString();
                if (id == 20093)
                {
                    result["boolean"] = value.ToBool();
                }

                if (id is 20011 or 11011 or 10901)
                {
                    using MultiLangString text = value.ToMultiLangString();
                    result["en_US"] = text.GetStringToDisplay(ISOCode.Language.L_en_US);
                    result["de_DE"] = text.GetStringToDisplay(ISOCode.Language.L_de_DE);
                }
            }

            result["available"] = true;
        }
        catch (PropertyNotFoundException)
        {
            // A36 / S063109: one absent property must not abort the remaining objects or the after snapshot.
            result["available"] = false;
            result["reason"] = "not available";
        }

        return result;
    }

    private static void Bind(PlaceHolder holder, StorableObject target, AnyPropertyId property, string variable)
    {
        holder.AddVariable(variable);
        using MultiLangString entry = Text("<" + variable + ">");
        holder.SetPropertyEntry(target, property, entry); // Adds the reference if needed (installed API docs).
    }

    private static void Set(PlaceHolder holder, string variable, string value, ISOCode.Language language = ISOCode.Language.L___)
    {
        using MultiLangString text = Text(value, language);
        holder.SetValue("Apply", variable, text);
    }

    private static MultiLangString Text(string value, ISOCode.Language language = ISOCode.Language.L___)
    {
        var text = new MultiLangString();
        text.AddString(language, value);
        return text;
    }

    private static AnyPropertyId Property(int id, int index)
    {
        AnyPropertyId property = id;
        property.Index = checked((short)index);
        return property;
    }

    private int Counter(Page page)
    {
        PagePropertyList properties = (PagePropertyList)Properties(page);
        return Counter(properties);
    }

    private static int Counter(PagePropertyList properties)
    {
        using PropertyValue value = properties.PAGE_COUNTER;
        return !value.IsEmpty && int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int counter) ? counter : -1;
    }

    private Page? Find(int counter)
    {
        using var finder = new DMObjectsFinder(project);
        return finder.GetPages(null).SingleOrDefault(p => Counter(p) == counter);
    }

    private Page NewPage(int counter)
    {
        using var finder = new DMObjectsFinder(project);
        Page template = finder.GetPages(null).First(p => p.PageType == DocumentTypeManager.DocumentType.Circuit);
        PagePropertyList source = (PagePropertyList)Properties(template);
        using var name = new PagePropertyList();
        foreach (Eplan.EplApi.DataModel.Properties.Page part in new[]
        {
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_FUNCTIONALASSIGNMENT,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_PLANT,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_PLACEOFINSTALLATION,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_LOCATION,
            Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_DOCTYPE,
        })
        {
            using PropertyValue value = source[part];
            if (!value.IsEmpty)
            {
                name[part] = value;
            }
        }

        name.PAGE_COUNTER = counter.ToString(CultureInfo.InvariantCulture);
#pragma warning disable CA2000 // Persistent page, owned by EPLAN (A39); only property wrappers are disposed.
        var page = new Page();
#pragma warning restore CA2000
        page.Create(project, DocumentTypeManager.DocumentType.Circuit, name);
        return page;
    }

    private UniversalPropertyList Properties(StorableObject target)
    {
        // EplanPropertySetSession's type/oid key: one list per object for the entire main-thread invocation.
        string[] identifier = target.ToStringIdentifier().Split('/');
        if (identifier.Length != 4 || !int.TryParse(identifier[1], NumberStyles.None, CultureInfo.InvariantCulture, out int type))
        {
            throw new InvalidOperationException("Unknown EPLAN object identifier format.");
        }

        var key = (type, target.ObjectIdentifier);
        if (!_lists.TryGetValue(key, out UniversalPropertyList? list))
        {
            list = target switch
            {
                Page page => page.Properties,
                Function function => function.Properties,
                Project owner => owner.Properties,
                _ => throw new InvalidOperationException("No property list for this object type."),
            };
            _lists.Add(key, list);
        }

        return list;
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        {
            foreach (UniversalPropertyList list in _lists.Values)
            {
                try
                {
                    list.Dispose();
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }
        }
        finally
        {
            _lists.Clear();
        }

        if (failures.Count > 0)
        {
            throw new AggregateException("Property list release failed.", failures);
        }
    }
}

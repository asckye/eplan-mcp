using System.Diagnostics;
using System.Globalization;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S34;

public sealed partial class SpikeS34Action
{
    private static Dictionary<long, JObject> DeviceListItems(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        var items = new Dictionary<long, JObject>();
        // DeviceListEntry has no public constructor/Create in the catalog; GetAll enumerates factory results.
        foreach (StorableObject item in finder.GetAll<DeviceListEntry>(true))
        {
            // Borrowed from EPLAN; only the finder above is owned by this method.
            long id = item.ObjectIdentifier;
            var row = new JObject { ["id"] = id };
            items.Add(id, row);
            Capture(row, "partNumber", () => new JValue(((DeviceListEntry)item).PartNumber));
        }

        return items;
    }

    private static void SeededDeviceListRoundTrip(JObject report, JArray attempts, Project project, string path, Dictionary<string, string> baseline, string root, string extension, Dictionary<string, string> candidate)
    {
        var fixture = new JObject
        {
            ["id"] = "seeded-" + attempts.Count.ToString("D2", CultureInfo.InvariantCulture),
            ["parameters"] = JObject.FromObject(candidate),
            ["factory"] = "DeviceService.CreateDeviceListItem(Project, partNumber, partVariant)",
            ["scope"] = "One SafetyPoint contains creation, export, deletion and import; always rolled back.",
        };
        if (report["deviceListFixtures"] is not JArray fixtures)
        {
            fixtures = [];
            report["deviceListFixtures"] = fixtures;
        }

        fixtures.Add(fixture);
        var actionResults = new List<JObject>();
        string folder = Path.Combine(root, fixture.Value<string>("id")!);
        try
        {
            Directory.CreateDirectory(folder);
            Capture(fixture, "before", () => Counts(project));
            Capture(fixture, "projectFilesBefore", () => FileSummary(Files(path), baseline, false));
            Dictionary<long, JObject> original = DeviceListItems(project);
            fixture["entriesBefore"] = Listed(original.Values, 8);
            using (SafetyPoint safety = SafetyPoint.Create())
            {
                try
                {
                    using var service = new DeviceService();
                    HashSet<long> created = SeedDeviceList(project, service, original, fixture);
                    Dictionary<long, JObject> seeded = DeviceListItems(project);
                    // A factory can fail after creating data. Track every new identity in this locked scope.
                    created.UnionWith(seeded.Keys.Where(id => !original.ContainsKey(id)));
                    fixture["createdCount"] = created.Count;
                    Capture(fixture, "afterSeed", () => Counts(project));
                    fixture["entriesAfterSeed"] = Listed(seeded.Values, 8);
                    if (created.Count == 0)
                    {
                        fixture["skipped"] = "No new device-list entries could be created. The original r1 round trip remains in attempts.";
                        return;
                    }

                    string expected = Path.Combine(folder, "roundtrip" + extension);
                    var export = new Dictionary<string, string>(candidate, StringComparer.Ordinal) { ["EXPORTFILE"] = expected };
                    JObject exported = FixtureAction(attempts, actionResults, fixture, project, path, folder, "EXPORT", export);
                    string? source = ExportedFixtureFile(exported, expected, extension);
                    if (source is null)
                    {
                        fixture["importSkipped"] = "The seeded export failed or produced no unambiguous nonempty file.";
                        return;
                    }

                    var deletions = new JArray();
                    fixture["deletions"] = deletions;
                    using (var finder = new DMObjectsFinder(project))
                    {
                        foreach (StorableObject item in finder.GetAll<DeviceListEntry>(true))
                        {
                            var deletion = new JObject();
                            try
                            {
                                long id = item.ObjectIdentifier;
                                if (!created.Contains(id))
                                {
                                    continue; // Never remove a pre-existing entry, even if its part number matches.
                                }

                                deletion["id"] = id;
                                // Delete only the fixture's new entries; do not Dispose EPLAN-owned wrappers.
                                service.DeleteDeviceListItem((DeviceListEntry)item);
                                deletion["returned"] = true;
                            }
                            catch (Exception ex)
                            {
                                deletion["error"] = Describe(ex);
                            }

                            deletions.Add(deletion);
                        }
                    }

                    Dictionary<long, JObject> afterDelete = DeviceListItems(project);
                    Capture(fixture, "afterDelete", () => Counts(project));
                    fixture["entriesAfterDelete"] = Listed(afterDelete.Values, 8);
                    if (created.Any(afterDelete.ContainsKey))
                    {
                        fixture["importSkipped"] = "At least one created entry could not be deleted; do not mask this by importing over it.";
                        return;
                    }

                    var import = new Dictionary<string, string>(candidate, StringComparer.Ordinal) { ["IMPORTFILE"] = source };
                    JObject imported = FixtureAction(attempts, actionResults, fixture, project, path, folder, "IMPORT", import);
                    imported["roundTripSource"] = source;
                    Capture(fixture, "entriesAfterImport", () => Listed(DeviceListItems(project).Values, 8));
                }
                catch (Exception ex)
                {
                    fixture["fixtureException"] = Describe(ex);
                }
                finally
                {
                    Capture(fixture, "after", () => Counts(project));
                    Capture(fixture, "projectFilesAfter", () => FileSummary(Files(path), baseline, false));
                    var rollbackClock = Stopwatch.StartNew();
                    try
                    {
                        safety.Rollback();
                        fixture["rollbackReturned"] = true;
                    }
                    catch (Exception ex)
                    {
                        fixture["rollbackException"] = Describe(ex);
                    }
                    finally
                    {
                        fixture["rollbackMs"] = rollbackClock.Elapsed.TotalMilliseconds;
                    }

                    Capture(fixture, "afterRollback", () => Counts(project));
                    Capture(fixture, "entriesAfterRollback", () => Listed(DeviceListItems(project).Values, 8));
                }
            }
        }
        catch (Exception ex)
        {
            fixture["scopeException"] = Describe(ex);
        }
        finally
        {
            Capture(fixture, "afterSafetyPointDisposed", () => Counts(project));
            Capture(fixture, "projectFilesAfterRollback", () => FileSummary(Files(path), baseline, false));
            Capture(fixture, "files", () => OutputFiles(folder));
            foreach (JObject result in actionResults)
            {
                foreach (string key in new[] { "afterRollback", "afterSafetyPointDisposed", "rollbackReturned", "rollbackMs", "rollbackException", "scopeException" })
                {
                    if (fixture[key] is JToken value)
                    {
                        result[key] = value.DeepClone();
                    }
                }
            }
        }
    }

    private static HashSet<long> SeedDeviceList(Project project, DeviceService service, Dictionary<long, JObject> original, JObject fixture)
    {
        var parts = new List<KeyValuePair<string, string>>();
        var discoveryErrors = new JArray();
        foreach (Article article in project.Articles)
        {
            try
            {
                string part = article.PartNr;
                if (!string.IsNullOrEmpty(part))
                {
                    parts.Add(new KeyValuePair<string, string>(part, article.VariantNr));
                }
            }
            catch (Exception ex)
            {
                if (discoveryErrors.Count < 6)
                {
                    discoveryErrors.Add(Describe(ex));
                }
            }
        }

        fixture["partDiscoveryErrors"] = discoveryErrors;
        var creationAttempts = new JArray();
        fixture["creationAttempts"] = creationAttempts;
        var created = new HashSet<long>();
        foreach (KeyValuePair<string, string> part in parts.OrderBy(p => p.Key, StringComparer.Ordinal).GroupBy(p => p.Key, StringComparer.Ordinal).Select(g => g.First()).Take(4))
        {
            var attempt = new JObject { ["partNumber"] = part.Key, ["variant"] = part.Value };
            creationAttempts.Add(attempt);
            try
            {
                // The project owns the created entry too; the outer SafetyPoint restores the data.
                DeviceListEntry entry = service.CreateDeviceListItem(project, part.Key, part.Value);
                long id = entry.ObjectIdentifier;
                attempt["id"] = id;
                attempt["newIdentity"] = !original.ContainsKey(id) && created.Add(id);
                attempt["partNumberReadBack"] = entry.PartNumber;
            }
            catch (Exception ex)
            {
                attempt["error"] = Describe(ex);
            }

            // Include data created before an exception, and stop at two observed new entries.
            Dictionary<long, JObject> current = DeviceListItems(project);
            created.UnionWith(current.Keys.Where(id => !original.ContainsKey(id)));
            attempt["observedNewEntries"] = created.Count;

            if (created.Count >= 2)
            {
                break;
            }
        }

        fixture["createdCount"] = created.Count;
        fixture["partSource"] = "Up to four distinct existing project part numbers/variants; stop after two new entries. No shared parts database writes.";
        return created;
    }

    private static JObject FixtureAction(JArray attempts, List<JObject> actionResults, JObject fixture, Project project, string path, string folder, string type, Dictionary<string, string> options)
    {
        var parameters = new Dictionary<string, string>(options, StringComparer.Ordinal) { ["TYPE"] = type, ["PROJECTNAME"] = path };
        var result = new JObject { ["action"] = "devicelist", ["parameters"] = JObject.FromObject(parameters), ["fixture"] = fixture["id"]!.DeepClone(), ["returned"] = JValue.CreateNull(), ["elapsedMs"] = JValue.CreateNull() };
        attempts.Add(result);
        actionResults.Add(result);
        Capture(result, "before", () => Counts(project));
        int bookmark = -1;
        Capture(result, "systemMessageBookmark", () => { bookmark = new Eplan.EplApi.Base.SysMessagesCollection().Count; return new JValue(bookmark); });
        ExecuteBuiltIn(result, "devicelist", parameters);
        Capture(result, "after", () => Counts(project));
        Capture(result, "files", () => OutputFiles(folder));
        Capture(result, "systemMessages", () => MessagesSince(bookmark));
        Capture(result, "mainWindowDisabledAfter", () => new JValue(MainWindowDisabled()));
        return result;
    }

    private static string? ExportedFixtureFile(JObject result, string expected, string extension)
    {
        if (result.Value<bool?>("returned") != true || result["exception"] is not null)
        {
            return null;
        }

        if (File.Exists(expected) && new FileInfo(expected).Length > 0)
        {
            return expected;
        }

        string[] files = Directory.GetFiles(Path.GetDirectoryName(expected)!, "*", SearchOption.TopDirectoryOnly)
            .Where(f => string.Equals(Path.GetExtension(f), extension, StringComparison.OrdinalIgnoreCase) && new FileInfo(f).Length > 0).ToArray();
        return files.Length == 1 ? files[0] : null;
    }
}

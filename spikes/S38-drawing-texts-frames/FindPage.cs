using System.Diagnostics;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S38;

public sealed partial class SpikeS38Action
{
    private static JObject FindPage(Project project)
    {
        var watch = Stopwatch.StartNew();
        var result = new JObject();
        var matches = new JArray();
        var errors = new JArray();
        result["pages"] = matches;
        result["pageErrors"] = errors;
        Page[] pages = project.Pages;
        int scanned = 0;
        int freePages = 0;
        int multilingualPages = 0;
        int readyPages = 0;
        foreach (Page page in pages.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            // Leave time for final design-file hashing and report publication before the driver's 180 s wait ends.
            if (watch.Elapsed > TimeSpan.FromSeconds(90)) { result["stopped"] = "90 s scan budget reached; unscanned pages are not classified."; break; }
            scanned++;
            var pageWatch = Stopwatch.StartNew();
            JObject row = Identity(page);
            row["page"] = page.Name;
            var texts = new JArray();
            row["texts"] = texts;
            try
            {
                // Finder functions and symbol property placements are unnecessary for exact Graphics.Text candidates.
                foreach ((Placement item, string origin) in Placements(page, row, includeRelated: false))
                {
                    if (origin != "page" || item.GetType() != typeof(Text)) { continue; }
                    JObject text = Identity(item);
                    text["origin"] = origin;
                    texts.Add(text);
                    JToken content = Evidence.Capture(text, "content", () => Content((Text)item));
                    if (content["error"] is null)
                    {
                        int count = ((JObject)content).Count;
                        text["languageCount"] = count;
                        text["languages"] = new JArray(((JObject)content).Properties().Select(p => p.Name));
                    }
                }
                row["freeTextCount"] = texts.Count;
                row["singleLanguageTextCount"] = texts.Count(t => t.Value<int?>("languageCount") == 1);
                row["multiLanguageTextCount"] = texts.Count(t => t.Value<int?>("languageCount") >= 2);
                row["emptyTextCount"] = texts.Count(t => t.Value<int?>("languageCount") == 0);
                row["complete"] = ((JArray)row["traversalErrors"]!).Count == 0 && texts.All(t => t["content"]?["error"] is null);
                row["replaceReady"] = row.Value<bool>("complete") && row.Value<int>("singleLanguageTextCount") > 0 && row.Value<int>("multiLanguageTextCount") > 0;
                if (texts.Count > 0) { matches.Add(row); freePages++; }
                if (row.Value<int>("multiLanguageTextCount") > 0) { multilingualPages++; }
                if (row.Value<bool>("replaceReady")) { readyPages++; }
                if (row.Value<bool>("complete") != true) { errors.Add(row.DeepClone()); }
            }
            catch (Exception ex) { row["error"] = Evidence.Error(ex); errors.Add(row); }
            finally { row["elapsedMs"] = pageWatch.Elapsed.TotalMilliseconds; }
        }
        result["totalPages"] = pages.Length;
        result["scannedPages"] = scanned;
        result["unscannedPages"] = pages.Length - scanned;
        result["pagesWithFreeTexts"] = freePages;
        result["pagesWithMultiLanguageTexts"] = multilingualPages;
        result["replaceReadyPages"] = readyPages;
        result["complete"] = scanned == pages.Length && errors.Count == 0;
        result["elapsedMs"] = watch.Elapsed.TotalMilliseconds;
        result["selection"] = "Exact Graphics.Text on the page, excluding PlotFrame.SubPlacements and derived path/placeholder texts. replaceReady requires separate single-language and multilingual objects. All matching pages and text contents are retained.";
        return result;
    }
}

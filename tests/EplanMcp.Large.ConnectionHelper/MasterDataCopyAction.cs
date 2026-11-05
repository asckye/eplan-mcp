using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.MasterData;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Points this variant's parts and translation database settings at copies below scratch, and reads what EPLAN resolves.</summary>
public sealed class MasterDataCopyAction : IEplAction
{
    private const string PartsSetting = "USER.PartsManagementGui.Database";
    private const string TranslateSetting = "USER.TRANSLATEGUI.DATABASE_NAME";

    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeMasterDataCopy"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string file = "";
        oActionCallingContext.GetParameter("REQUEST", ref file);
        JObject input = JObject.Parse(File.ReadAllText(file));
        var result = new JObject();
        try
        {
            using var settings = new Settings();
            if ((string?)input["op"] == "set")
            {
                string parts = (string)input["parts"]!, translate = (string)input["translate"]!;
                foreach (string path in new[] { parts, translate })
                {
                    if (!path.Contains(@"\scratch\masterdata-copy\") || !File.Exists(path))
                    { throw new InvalidOperationException("Only an existing copy below scratch is permitted: " + path); }
                }
                result["partsBefore"] = settings.GetStringSetting(PartsSetting, 0);
                result["translateBefore"] = settings.GetStringSetting(TranslateSetting, 0);
                settings.SetStringSetting(PartsSetting, parts, 0);
                settings.SetStringSetting(TranslateSetting, translate, 0);
            }
            result["partsSetting"] = settings.GetStringSetting(PartsSetting, 0);
            result["translateSetting"] = settings.GetStringSetting(TranslateSetting, 0);
            result["partsResolved"] = MDPartsManagement.SelectedPartsDatabaseAsString;
            using var translator = new Translate();
            result["translateResolved"] = translator.TranslateDatabase;
        }
        catch (Exception error) { result["exception"] = error.GetType().Name + " " + error.Message; }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }
}

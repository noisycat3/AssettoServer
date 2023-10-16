using System.Collections.Generic;
using AssettoServer.Shared.Utils;
using AssettoServer.Utils;
using IniParser;
using IniParser.Model;
using JetBrains.Annotations;

namespace AssettoServer.Server.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class EntryList
{
    [IniSection("CAR")] public IReadOnlyList<Entry> Cars { get; init; } = new List<Entry>();
    
    [UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
    public class Entry
    {
        [IniField("MODEL")] public string Model { get; init; } = "";
        [IniField("SKIN")] public string? Skin { get; init; }
        [IniField("BALLAST")] public int Ballast { get; init; }
        [IniField("RESTRICTOR")] public int Restrictor { get; init; }
        [IniField("GUID")] public string Guid { get; init; } = "";
        [IniField("AI")] public bool AiEnable { get; init; }
    }
    
    public static EntryList FromFile(string path)
    {
        var parser = new FileIniDataParser();
        IniData data = parser.ReadFile(path);
        return data.DeserializeObject<EntryList>();
    }
}

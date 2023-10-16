using AssettoServer.Shared.Model;
using AssettoServer.Shared.Utils;
using AssettoServer.Utils;
using JetBrains.Annotations;

namespace AssettoServer.Server.Configuration;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class SessionConfiguration : ISessionConfig
{
    public int Id { get; set; }
    public SessionType Type { get; set; }

    [IniField("NAME")] public string? Name { get; set; } = "";
    [IniField("TIME")] public int Time { get; set; }
    [IniField("LAPS")] public int Laps { get; set; }
    [IniField("WAIT_TIME")] public uint WaitTime { get; set; }
    [IniField("IS_OPEN")] public bool IsOpen { get; set; }
    [IniField("INFINITE")] public bool Infinite { get; set; }

    public bool IsTimedRace => (Time > 0 && Laps == 0);
}

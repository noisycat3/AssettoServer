using System.Linq;
using AssettoServer.Commands.Attributes;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using Qmmands;

namespace AssettoServer.Commands.Modules;

[RequireAdmin]
[UsedImplicitly(ImplicitUseKindFlags.Access, ImplicitUseTargetFlags.WithMembers)]
public class AiTrafficModule : ACModuleBase
{
    private readonly ACServerConfiguration _configuration;
    
    public AiTrafficModule(ACServerConfiguration configuration)
    {
        _configuration = configuration;
    }

    [Command("setaioverbooking")]
    public void SetAiOverbooking(int count)
    {
        if (!_configuration.Extra.EnableAi)
        {
            Reply("AI disabled");
            return;
        }

        // OLD AI
        //foreach (var aiCar in _entryCarManager.EntryCars.Where(car => car.AiControlled && car.Client == null))
        //{
        //    aiCar.SetAiOverbooking(count);
        //}
        Reply($"AI overbooking set to {count}");
    }
}

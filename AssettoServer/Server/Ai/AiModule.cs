using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.OpenSlotFilters;
using AssettoServer.Server.Plugin;
using Autofac;
using Microsoft.Extensions.Hosting;

namespace AssettoServer.Server.Ai;

public class AiModule : Module
{
    private readonly ACServerConfiguration _configuration;

    public AiModule(ACServerConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override void Load(ContainerBuilder builder)
    {
        // OLD AI: Register AiState
        //builder.RegisterType<AiState>().AsSelf();

        if (_configuration.Extra.EnableAi)
        {
            // OLD AI: Register AiBehavior, AiUpdater
            //builder.RegisterType<AiBehavior>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
            //builder.RegisterType<AiUpdater>().AsSelf().SingleInstance().AutoActivate();
            builder.RegisterType<AiSlotFilter>().As<IOpenSlotFilter>();
            builder.RegisterType<AiSplineWriter>().AsSelf();
            builder.RegisterType<FastLaneParser>().AsSelf();
            builder.RegisterType<AiSplineLocator>().AsSelf();
            builder.Register((AiSplineLocator locator) => locator.Locate()).AsSelf().SingleInstance();
        }
    }
}

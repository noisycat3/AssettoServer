using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Plugin;
using AssettoServer.Shared.Services;
using Autofac;

namespace RaceChallengePlugin;

public class RaceChallengeModule : AssettoServerModule
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<RaceChallengePlugin>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
        builder.RegisterType<EntryCarRace>().AsSelf();
        builder.RegisterType<Race>().AsSelf();
    }
}

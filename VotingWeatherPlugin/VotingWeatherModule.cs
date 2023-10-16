using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Plugin;
using AssettoServer.Shared.Services;
using Autofac;

namespace VotingWeatherPlugin;

public class VotingWeatherModule : AssettoServerModule<VotingWeatherConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<VotingWeather>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
    }
}

using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Plugin;
using AssettoServer.Shared.Services;
using Autofac;

namespace LiveWeatherPlugin;

public class LiveWeatherModule : AssettoServerModule<LiveWeatherConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<LiveWeatherProvider>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
    }
}

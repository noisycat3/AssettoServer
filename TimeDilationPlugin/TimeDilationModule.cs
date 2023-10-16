using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Plugin;
using AssettoServer.Shared.Services;
using Autofac;

namespace TimeDilationPlugin;

public class TimeDilationModule : AssettoServerModule<TimeDilationConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<TimeDilationPlugin>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
    }
}

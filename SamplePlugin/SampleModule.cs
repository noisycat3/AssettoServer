using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Plugin;
using AssettoServer.Shared.Services;
using Autofac;

namespace SamplePlugin;

public class SampleModule : AssettoServerModule<SampleConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<Sample>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
    }
}

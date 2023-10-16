using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Plugin;
using AssettoServer.Shared.Services;
using Autofac;

namespace ReportPlugin;

public class ReportPluginModule : AssettoServerModule<ReportConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ReportPlugin>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
    }
}

﻿using AssettoServer.Shared.Plugin;
using AssettoServer.Shared.Services;
using Autofac;

namespace AutoModerationPlugin;

public class AutoModerationModule : AssettoServerModule<AutoModerationConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AutoModerationPlugin>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
        builder.RegisterType<EntryCarAutoModeration>().AsSelf();
    }
}

using System;
using System.Reflection;
using AssettoServer.Shared.Plugin;

namespace AssettoServer.Server.Plugin;

internal class Plugin
{
    public string Name { get; }
    public Assembly Assembly { get; }
    public AssettoServerModule Instance { get; }
    public Type? ConfigurationType { get; }
    public Type? ValidatorType { get; }

    public Plugin(string name, Assembly assembly, AssettoServerModule instance, Type? configurationType,
        Type? validatorType)
    {
        Name = name;
        Assembly = assembly;
        Instance = instance;
        ConfigurationType = configurationType;
        ValidatorType = validatorType;
    }
}

internal class PluginV2
{
    public string Name { get; }
    public Assembly Assembly { get; }
    public AssettoServerPlugin PluginInstance { get; }

    public PluginV2(string name, Assembly assembly, AssettoServerPlugin instance)
    {
        Name = name;
        Assembly = assembly;
        PluginInstance = instance;
    }
}

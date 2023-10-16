﻿using System;
using System.Reflection;
using AssettoServer.Shared.Plugin;

namespace AssettoServer.Server.Plugin;

public class Plugin
{
    public string Name { get; }
    public Assembly Assembly { get; }
    public AssettoServerModule Instance { get; }
    public Type? ConfigurationType { get; }
    public Type? ValidatorType { get; }

    public Plugin(string name, Assembly assembly, AssettoServerModule instance, Type? configurationType, Type? validatorType)
    {
        Name = name;
        Assembly = assembly;
        Instance = instance;
        ConfigurationType = configurationType;
        ValidatorType = validatorType;
    }
}

﻿using System;
using System.Collections.Generic;
using System.IO;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Configuration;
using AssettoServer.Shared.Plugin;
using AssettoServer.Shared.Utils;
using McMaster.NETCore.Plugins;
using Serilog;

namespace AssettoServer.Server.Plugin;

public class ACPluginLoader
{
    public Dictionary<string, PluginLoader> AvailablePlugins { get; } = new();
    public List<Plugin> LoadedPlugins { get; } = new();

    public ACPluginLoader(bool loadFromWorkdir)
    {
        if (loadFromWorkdir)
        {
            var dir = Path.Join(Directory.GetCurrentDirectory(), "plugins");
            if (Directory.Exists(dir))
            {
                ScanDirectory(dir);
            }
            else
            {
                Directory.CreateDirectory(dir);
            }
        }
        
        string pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        ScanDirectory(pluginsDir);
    }

    public void ScanDirectory(string path)
    {
        foreach (string dir in Directory.GetDirectories(path))
        {
            string dirName = Path.GetFileName(dir);
            string pluginDll = Path.Combine(dir, dirName + ".dll");
            if (File.Exists(pluginDll) && !AvailablePlugins.ContainsKey(dirName))
            {
                Log.Verbose("Found plugin {PluginName}, {PluginPath}", dirName, pluginDll);

                var loader = PluginLoader.CreateFromAssemblyFile(
                    pluginDll,
                    config => config.PreferSharedTypes = true);
                AvailablePlugins.Add(dirName, loader);
            }
        }
    }

    public void LoadPlugin(string name)
    {
        if (!AvailablePlugins.TryGetValue(name, out var loader))
        {
            throw new ConfigurationException($"No plugin found with name {name}");
        }
        
        var assembly = loader.LoadDefaultAssembly();

        foreach (var type in assembly.GetTypes())
        {
            if (typeof(AssettoServerModule).IsAssignableFrom(type) && !type.IsAbstract)
            {
                AssettoServerModule instance = Activator.CreateInstance(type) as AssettoServerModule ?? throw new InvalidOperationException("Could not create plugin instance");

                Type? configType = null;
                Type? validatorType = null;
                var baseType = type.BaseType!;
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(AssettoServerModule<>))
                {
                    configType = baseType.GetGenericArguments()[0];

                    foreach (var iface in configType.GetInterfaces())
                    {
                        if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IValidateConfiguration<>))
                        {
                            validatorType = iface.GetGenericArguments()[0];
                        }
                    }
                }

                LoadedPlugins.Add(new Plugin(name, assembly, instance, configType, validatorType));
            }
        }
        
        Log.Information("Loaded plugin {PluginName}", name);
    }
}

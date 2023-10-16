﻿using AssettoServer.Server.Configuration;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AssettoServer.Network.Http;
using AssettoServer.Shared.Utils;
using AssettoServer.Utils;
using Autofac.Extensions.DependencyInjection;
using CommandLine;
using FluentValidation;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;
using Serilog.Templates;
using Parser = CommandLine.Parser;

namespace AssettoServer;

internal static class Program
{
    [UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
    private class Options
    {
        [Option('p', "preset", Required = false, SetName = "AssettoServer", HelpText = "Configuration preset")]
        public string Preset { get; set; } = "";

        [Option('c', Required = false, SetName = "Content Manager compatibility", HelpText = "Path to server configuration")]
        public string ServerCfgPath { get; set; } = "";

        [Option('e', Required = false, SetName = "Content Manager compatibility", HelpText = "Path to entry list")]
        public string EntryListPath { get; set; } = "";

        [Option("plugins-from-workdir", Required = false, HelpText = "Additionally load plugins from working directory")]
        public bool LoadPluginsFromWorkdir { get; set; } = false;
    }
        
    internal static async Task Main(string[] args)
    {
        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) =>
        {
            foreach (var attr in member!.GetCustomAttributes(true))
            {
                if (attr is IniFieldAttribute iniAttr)
                {
                    return iniAttr.Key;
                }
            }
            return member.Name;
        }; 
            
        var options = Parser.Default.ParseArguments<Options>(args).Value;
        if (options == null) return;
        
        var isContentManager = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                               && !string.IsNullOrEmpty(options.ServerCfgPath)
                               && !string.IsNullOrEmpty(options.EntryListPath);

        if (isContentManager)
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        string logPrefix = string.IsNullOrEmpty(options.Preset) ? "log" : options.Preset;
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("AssettoServer.Network.Http.Authentication.ACClientAuthenticationHandler", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Grpc", LogEventLevel.Warning)
            .WriteTo.Async(a =>
            {
                if (isContentManager)
                {
                    a.Console(new ExpressionTemplate(
                        "{#if @l = 'Debug'}…{#else if @l = 'Warning'}‽{#else if @l = 'Error' or @l = 'Fatal'}▲{#else} {#end} {@m}\n{@x}"));
                }
                else
                {
                    a.Console();
                }
            })
            .WriteTo.File($"logs/{logPrefix}-.txt",
                rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Log.Information("AssettoServer {Version}", ThisAssembly.AssemblyInformationalVersion);
        
        try
        {
            var config = new ACServerConfiguration(options.Preset, options.ServerCfgPath, options.EntryListPath,
                options.LoadPluginsFromWorkdir);

            if (config.Extra.LokiSettings != null
                && !string.IsNullOrEmpty(config.Extra.LokiSettings.Url)
                && !string.IsNullOrEmpty(config.Extra.LokiSettings.Login)
                && !string.IsNullOrEmpty(config.Extra.LokiSettings.Password))
            {
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("AssettoServer.Network.Http.Authentication.ACClientAuthenticationHandler", LogEventLevel.Warning)
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("Grpc", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithMachineName()
                    .Enrich.WithProperty("Preset", options.Preset)
                    .WriteTo.GrafanaLoki(config.Extra.LokiSettings.Url,
                        credentials: new LokiCredentials
                        {
                            Login = config.Extra.LokiSettings.Login,
                            Password = config.Extra.LokiSettings.Password
                        },
                        useInternalTimestamp: true,
                        textFormatter: new LokiJsonTextFormatter(),
                        propertiesAsLabels: new[] { "MachineName", "Preset" })
                    .WriteTo.Async(a => a.Console())
                    .WriteTo.File($"logs/{logPrefix}-.txt",
                        rollingInterval: RollingInterval.Day)
                    .CreateLogger();
            }
            
            if (!string.IsNullOrEmpty(options.Preset))
            {
                Log.Information("Using preset {Preset}", options.Preset);
            }

            var host = Host.CreateDefaultBuilder()
                .UseServiceProviderFactory(new AutofacServiceProviderFactory())
                .UseSerilog()
                .ConfigureWebHostDefaults(webHostBuilder =>
                {
                    webHostBuilder.ConfigureKestrel(serverOptions => serverOptions.AllowSynchronousIO = true)
                        .UseStartup(_ => new Startup(config))
                        .UseUrls($"http://0.0.0.0:{config.Server.HttpPort}");
                })
                .Build();
            
            Metrics.ConfigureMeterAdapter(adapterOptions =>
            {
                // Disable a bunch of verbose / unnecessary default metrics
                adapterOptions.InstrumentFilterPredicate = inst => inst.Name != "kestrel.active_connections"
                                                                   && inst.Name != "http.server.active_requests"
                                                                   && inst.Name != "kestrel.queued_connections"
                                                                   && inst.Name != "http.server.request.duration"
                                                                   && inst.Name != "kestrel.connection.duration"
                                                                   && inst.Name != "aspnetcore.routing.match_attempts"
                                                                   && inst.Name != "dns.lookups.duration"
                                                                   && !inst.Name.StartsWith("http.client.");
            });
            
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Error starting server");
            await Log.CloseAndFlushAsync();
            ExceptionHelper.PrintExceptionHelp(ex, isContentManager);
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        Log.Fatal((Exception)args.ExceptionObject, "Unhandled exception occurred");
        Log.CloseAndFlush();
        Environment.Exit(1);
    }
}

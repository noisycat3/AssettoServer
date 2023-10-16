using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Configuration;
using AssettoServer.Network.Udp;
using AssettoServer.Server.Blacklist;
using AssettoServer.Server.GeoParams;
using AssettoServer.Server.Plugin;
using AssettoServer.Server.Weather;
using AssettoServer.Server.Whitelist;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Shared.Network.Packets.Shared;
using AssettoServer.Utils;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Serilog;
using AssettoServer.Shared.Services;
using AssettoServer.Shared.Utils;

namespace AssettoServer.Server;

public class ACServer : CriticalBackgroundService, IACServer
{
    private readonly ACServerConfiguration _configuration;
    private readonly IBlacklistService _blacklist;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly GeoParamsManager _geoParamsManager;
    private readonly ChecksumManager _checksumManager;
    private readonly List<IHostedService> _autostartServices;
    private readonly IHostApplicationLifetime _applicationLifetime;

    /// <summary>
    /// Fires on each server tick in the main loop. Don't do resource intensive / long running stuff in here!
    /// </summary>
    public event EventHandler<ACServer, EventArgs>? Update;

    public ACServer(
        ACServerConfiguration configuration,
        IBlacklistService blacklistService,
        IWhitelistService whitelistService,
        SessionManager sessionManager,
        EntryCarManager entryCarManager,
        WeatherManager weatherManager,
        GeoParamsManager geoParamsManager,
        ChecksumManager checksumManager,
        ACTcpServer tcpServer,
        ACUdpServer udpServer,
        CSPFeatureManager cspFeatureManager,
        CSPServerScriptProvider cspServerScriptProvider,
        IEnumerable<IAssettoServerAutostart> autostartServices,
        KunosLobbyRegistration kunosLobbyRegistration,
        IHostApplicationLifetime applicationLifetime) : base(applicationLifetime)
    {
        Log.Information("Starting server");
            
        _configuration = configuration;
        _blacklist = blacklistService;
        _sessionManager = sessionManager;
        _entryCarManager = entryCarManager;
        _geoParamsManager = geoParamsManager;
        _checksumManager = checksumManager;
        _applicationLifetime = applicationLifetime;

        _autostartServices = new List<IHostedService> { weatherManager, tcpServer, udpServer };
        _autostartServices.AddRange(autostartServices);
        _autostartServices.Add(kunosLobbyRegistration);

        blacklistService.Changed += OnBlacklistChanged;

        cspFeatureManager.Add(new CSPFeature { Name = "SPECTATING_AWARE" });
        cspFeatureManager.Add(new CSPFeature { Name = "LOWER_CLIENTS_SENDING_RATE" });
        cspFeatureManager.Add(new CSPFeature { Name = "EMOJI" });

        if (_configuration.Extra.EnableClientMessages)
        {
            if (_configuration.CSPTrackOptions.MinimumCSPVersion < 1937)
            {
                throw new ConfigurationException(
                    "Client messages need a minimum required CSP version of 0.1.77 (1937)");
            }
            
            cspFeatureManager.Add(new CSPFeature { Name = "CLIENT_MESSAGES", Mandatory = true });
            CSPClientMessageOutgoing.ChatEncoded = false;
        }

        if (_configuration.Extra.EnableUdpClientMessages)
        {
            cspFeatureManager.Add(new CSPFeature { Name = "CLIENT_UDP_MESSAGES" });
        }

        if (_configuration.Extra.EnableCustomUpdate)
        {
            cspFeatureManager.Add(new CSPFeature { Name = "CUSTOM_UPDATE" });
        }

        using (var streamReader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("AssettoServer.Server.Lua.assettoserver.lua")!))
        {
            cspServerScriptProvider.AddScript(streamReader.ReadToEnd(), "assettoserver.lua");
        }
    }

    internal void NotifyClientConnected(ACTcpClient client)
    {
        ClientConnected?.Invoke(this, new ClientConnectionEventArgs()
        {
            Client = client
        });
    }

    internal void NotifyClientDisconnected(ACTcpClient client)
    {
        ClientDisconnected?.Invoke(this, new ClientConnectionEventArgs()
        {
            Client = client
        });
    }

    private bool IsSessionOver()
    {
        if (_sessionManager.CurrentSession.Configuration.Type != SessionType.Race)
        {
            return _sessionManager.CurrentSession.TimeLeftMilliseconds < 0;
        }

        return false;
    }

    private void OnApplicationStopping()
    {
        Log.Information("Server shutting down");
        BroadcastPacket(new ChatMessage { SessionId = 255, Message = "*** Server shutting down ***" });

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tasks = new List<Task>();
        
        foreach (var service in _autostartServices)
        {
            tasks.Add(service.StopAsync(cts.Token));
        }

        try
        {
            Task.WaitAll(tasks.ToArray(), cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("Starting HTTP server on port {HttpPort}", _configuration.Server.HttpPort);
        
        _entryCarManager.Initialize();
        _checksumManager.Initialize();
        _sessionManager.Initialize();
        await _geoParamsManager.InitializeAsync();

        foreach (var service in _autostartServices)
        {
            await service.StartAsync(stoppingToken);
        }

        _ = _applicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
        var mainThread = new Thread(() => MainLoop(stoppingToken))
        {
            Name = "MainLoop",
            Priority = ThreadPriority.AboveNormal
        };
        mainThread.Start();
    }

    private void OnBlacklistChanged(IBlacklistService sender, EventArgs args)
    {
        _ = Task.Run(async () =>
        {
            foreach (EntryCarClient clientCar in _entryCarManager.ClientCars)
            {
                if (clientCar.Client == null)
                    continue;

                if (await sender.IsBlacklistedAsync(clientCar.Client.Guid))
                {
                    clientCar.Logger.Information("{ClientName} was banned after reloading blacklist", clientCar.Client.Name);
                    clientCar.Client.SendPacket(new KickCar { SessionId = clientCar.SessionId, Reason = KickReason.VoteBlacklisted });
                    _ = clientCar.DisconnectClient();
                }
            }
        });
    }

    private void MainLoop(CancellationToken stoppingToken)
    {
        int failedUpdateLoops = 0;
        int sleepMs = 1000 / _configuration.Server.RefreshRateHz;
        long nextTick = _sessionManager.ServerTimeMilliseconds;

        Log.Information("Starting update loop with an update rate of {RefreshRateHz}hz", _configuration.Server.RefreshRateHz);

        var updateLoopTimer = Metrics.CreateSummary("assettoserver_acserver_updateasync", "ACServer.UpdateAsync Duration", MetricDefaults.DefaultQuantiles);

        var updateLoopLateCounter = Metrics.CreateCounter("assettoserver_acserver_updateasync_late", "Total number of milliseconds the server was running behind");
        updateLoopLateCounter.Inc(0);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (updateLoopTimer.NewTimer())
                {
                    Update?.Invoke(this, EventArgs.Empty);

                    // Prepare and send the outgoing car updates
                    _entryCarManager.Update();
                    
                    if (IsSessionOver())
                    {
                        _sessionManager.NextSession();
                    }
                }

                if (_entryCarManager.HasConnectedClients)
                {
                    long tickDelta;
                    do
                    {
                        long currentTick = _sessionManager.ServerTimeMilliseconds;
                        tickDelta = nextTick - currentTick;

                        if (tickDelta > 0)
                            Thread.Sleep((int)tickDelta);
                        else if (tickDelta < -sleepMs)
                        {
                            if (tickDelta < -1000)
                                Log.Warning("Server is running {TickDelta}ms behind", -tickDelta);

                            updateLoopLateCounter.Inc(-tickDelta);
                            nextTick = 0;
                            break;
                        }
                    } while (tickDelta > 0);

                    if (nextTick == 0)
                        nextTick = _sessionManager.ServerTimeMilliseconds;

                    nextTick += sleepMs;
                }
                else
                {
                    nextTick = _sessionManager.ServerTimeMilliseconds;
                    Thread.Sleep(500);
                }

                failedUpdateLoops = 0;
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                if (failedUpdateLoops < 10)
                {
                    failedUpdateLoops++;
                    Log.Error(ex, "Something went wrong while trying to do a tick update");
                }
                else
                {
                    Log.Fatal(ex, "Cannot recover from update loop error, shutting down");
                    _applicationLifetime.StopApplication();
                }
            }
        }
    }

    // Public interface
    public void BroadcastPacket<TPacket>(TPacket packet, IClient? sender = null) where TPacket : IOutgoingNetworkPacket
    {
        foreach (EntryCarClient car in _entryCarManager.ClientCars)
        {
            if (car.Client == null)
                continue;

            if (car.Client.InGame && car.Client != sender)
                car.Client.SendPacket(packet);
        }
    }

    public void BroadcastPacketUdp<TPacket>(in TPacket packet, IClient? sender = null, float? range = null, bool skipSender = true) where TPacket : IOutgoingNetworkPacket
    {
        foreach (EntryCarClient car in _entryCarManager.ClientCars)
        {
            if (car.Client == null)
                continue;

            if (skipSender && car.Client == sender) 
                continue;

            if (!range.HasValue || (sender is ACTcpClient { ClientCar: {} clientCar } && clientCar.IsInRange(car.Status, range.Value)))
                car.Client.SendPacketUdp(in packet);
        }
    }

    public async Task KickAsync(IClient? client, string? reason = null, IClient? admin = null)
    {
        if (client == null)
            return;

        string? clientReason = reason != null ? $"You have been kicked for {reason}" : null;
        string broadcastReason = reason != null ? $"{client.Name} has been kicked from the server for {reason}." : $"{client.Name} has been kicked from the server.";

        await KickAsync(client, KickReason.Kicked, reason, clientReason, broadcastReason, admin);
    }

    public async Task BanAsync(IClient? client, string? reason = null, IClient? admin = null)
    {
        if (client == null) return;

        string clientReason = reason != null ? $"You have been banned for {reason}" : "You have been banned from the server";
        string broadcastReason = reason != null ? $"{client.Name} has been banned from the server for {reason}." : $"{client.Name} has been banned from the server.";

        await KickAsync(client, KickReason.VoteBlacklisted, reason, clientReason, broadcastReason, admin);
        await _blacklist.AddAsync(client.Guid);
        if (client.OwnerGuid.HasValue && client.Guid != client.OwnerGuid)
        {
            await _blacklist.AddAsync(client.OwnerGuid.Value);
        }
    }

    public async Task KickAsync(IClient? client, KickReason reason, string? auditReason = null, string? clientReason = null, string? broadcastReason = null, IClient? admin = null)
    {
        if (client is { IsDisconnectRequested: false })
        {
            if (broadcastReason != null)
            {
                BroadcastPacket(new ChatMessage { SessionId = 255, Message = broadcastReason });
            }

            if (clientReason != null)
            {
                client.SendPacket(new CSPKickBanMessageOverride { Message = clientReason });
            }

            client.SendPacket(new KickCar { SessionId = client.SessionId, Reason = reason });

            ClientAuditEventArgs args = new ClientAuditEventArgs
            {
                Client = client,
                Reason = reason,
                ReasonStr = broadcastReason,
                Admin = admin
            };

            if (reason is KickReason.Kicked or KickReason.VoteKicked)
            {
                client.Logger.Information("{ClientName} was kicked. Reason: {Reason}", client.Name, auditReason ?? "No reason given.");
                ClientKicked?.Invoke(this, args);
            }
            else if (reason is KickReason.VoteBanned or KickReason.VoteBlacklisted)
            {
                client.Logger.Information("{ClientName} was banned. Reason: {Reason}", client.Name, auditReason ?? "No reason given.");
                ClientBanned?.Invoke(this, args);
            }

            await client.BeginDisconnectAsync();
        }
    }

    public event EventHandler<IACServer, ClientConnectionEventArgs>? ClientConnected;
    public event EventHandler<IACServer, ClientAuditEventArgs>? ClientKicked;
    public event EventHandler<IACServer, ClientAuditEventArgs>? ClientBanned;
    public event EventHandler<IACServer, ClientConnectionEventArgs>? ClientDisconnected;
}

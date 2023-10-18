﻿#if DISABLE_STEAM

using System;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Configuration;
using Microsoft.Extensions.Hosting;

namespace AssettoServer.Server;

public class Steam : BackgroundService
{
    private readonly ACServerConfiguration _configuration;

    public Steam(ACServerConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration.Extra.UseSteamAuth)
        {
            throw new PlatformNotSupportedException("Steam is not supported on this platform");
        }

        return Task.CompletedTask;
    }

    internal ValueTask<bool> ValidateSessionTicketAsync(byte[]? sessionTicket, ulong guid, ACTcpClient client)
    {
        throw new PlatformNotSupportedException("Steam is not supported on this platform");
    }
}

#else

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Blacklist;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Services;
using Microsoft.Extensions.Hosting;
using Serilog;
using Steamworks;

namespace AssettoServer.Server;

public class Steam : CriticalBackgroundService
{
    private readonly ACServerConfiguration _configuration;
    private readonly IBlacklistService _blacklistService;
    private readonly CSPFeatureManager _cspFeatureManager;

    private bool _firstRun = true;
    
    public Steam(ACServerConfiguration configuration, IBlacklistService blacklistService, CSPFeatureManager cspFeatureManager, IHostApplicationLifetime applicationLifetime) : base(applicationLifetime)
    {
        _configuration = configuration;
        _blacklistService = blacklistService;
        _cspFeatureManager = cspFeatureManager;
    }

    private void Initialize()
    {
        var serverInit = new SteamServerInit("assettocorsa", "Assetto Corsa")
        {
            GamePort = _configuration.Server.UdpPort,
            Secure = true,
        }.WithQueryShareGamePort();

        try
        {
            SteamServer.Init(244210, serverInit);
        }
        catch
        {
            // ignored
        }

        try
        {
            SteamServer.LogOnAnonymous();
            SteamServer.OnSteamServersDisconnected += SteamServer_OnSteamServersDisconnected;
            SteamServer.OnSteamServersConnected += SteamServer_OnSteamServersConnected;
            SteamServer.OnSteamServerConnectFailure += SteamServer_OnSteamServerConnectFailure;
        }
        catch (Exception ex)
        {
            if (_firstRun) throw;
            Log.Error(ex, "Error trying to initialize SteamServer");
        }

        _firstRun = false;
    }

    internal void HandleIncomingPacket(byte[] data, IPEndPoint endpoint)
    {
        SteamServer.HandleIncomingPacket(data, data.Length, endpoint.Address.IpToInt32(), (ushort)endpoint.Port);

        while (SteamServer.GetOutgoingPacket(out var packet))
        {
            var dstEndpoint = new IPEndPoint((uint)IPAddress.HostToNetworkOrder((int)packet.Address), packet.Port);
            Log.Debug("Outgoing steam packet to {Endpoint}", dstEndpoint);
            //_server.UdpServer.Send(dstEndpoint, packet.Data, 0, packet.Size); TODO
        }
    }
    
    internal async ValueTask<bool> ValidateSessionTicketAsync(byte[]? sessionTicket, ulong guid, IClient client)
    {
        if (sessionTicket == null)
            return false;

        TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
        void TicketValidateResponse(SteamId playerSteamId, SteamId ownerSteamId, AuthResponse authResponse)
        {
            if (playerSteamId != guid)
                return;

            if (authResponse != AuthResponse.OK)
            {
                client.Logger.Information("Steam auth ticket verification failed ({AuthResponse}) for {ClientName}", authResponse, client.Name);
                taskCompletionSource.SetResult(false);
                return;
            }
            
            client.Disconnected += (_, _) =>
            {
                try
                {
                    SteamServer.EndSession(playerSteamId);
                }
                catch (Exception ex)
                {
                    client.Logger.Error(ex, "Error ending Steam session for client {ClientName}", client.Name);
                }
            };

            if (client is ACTcpClient tcpClient)
                tcpClient.OwnerGuid = ownerSteamId;

            if (playerSteamId != ownerSteamId)
            {
                if (_blacklistService.IsBlacklistedAsync(ownerSteamId).Result)
                {
                    client.Logger.Information("{ClientName} ({OwnerGuid}) is using Steam family sharing and game owner {OwnerSteamId} is blacklisted", client.Name, playerSteamId, ownerSteamId);
                    taskCompletionSource.SetResult(false);
                    return;
                }

                client.Logger.Information("{ClientName} ({OwnerGuid}) is using Steam family sharing, owner {OwnerSteamId}", client.Name, playerSteamId, ownerSteamId);
            }

            foreach (int appid in _configuration.Extra.ValidateDlcOwnership)
            {
                if (SteamServer.UserHasLicenseForApp(playerSteamId, appid) != UserHasLicenseForAppResult.HasLicense)
                {
                    client.Logger.Information("{ClientName} does not own required DLC {DlcId}", client.Name, appid);
                    taskCompletionSource.SetResult(false);
                    return;
                }
            }

            client.Logger.Information("Steam auth ticket verification succeeded for {ClientName}", client.Name);
            taskCompletionSource.SetResult(true);
        }

        bool validated = false;

        SteamServer.OnValidateAuthTicketResponse += TicketValidateResponse;

        if (!SteamServer.BeginAuthSession(sessionTicket, guid))
        {
            client.Logger.Information("Steam auth ticket verification failed for {ClientName}", client.Name);
            taskCompletionSource.SetResult(false);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        cts.Token.Register(() => taskCompletionSource.SetCanceled(cts.Token));

        try
        {
            validated = await taskCompletionSource.Task;
        }
        catch (TaskCanceledException)
        {
            client.Logger.Warning("Steam auth ticket verification timed out for {ClientName}", client.Name);
        }

        SteamServer.OnValidateAuthTicketResponse -= TicketValidateResponse;
        return validated;
    }

    private void SteamServer_OnSteamServersConnected()
    {
        Log.Information("Connected to Steam Servers");
    }

    private void SteamServer_OnSteamServersDisconnected(Result result)
    {
        Log.Error("Disconnected from Steam Servers ({Reason})", result);
        SteamServer.OnSteamServersConnected -= SteamServer_OnSteamServersConnected;
        SteamServer.OnSteamServersDisconnected -= SteamServer_OnSteamServersDisconnected;
        SteamServer.OnSteamServerConnectFailure -= SteamServer_OnSteamServerConnectFailure;

        try
        {
            SteamServer.LogOff();
        }
        catch
        {
            // ignored
        }

        try
        {
            SteamServer.Shutdown();
        }
        catch
        {
            // ignored
        }

        Initialize();
    }

    private void SteamServer_OnSteamServerConnectFailure(Result result, bool stillTrying)
    {
        Log.Error("Failed to connect to Steam servers ({Reason}), still trying = {StillTrying}", result, stillTrying);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_configuration.Extra.UseSteamAuth)
        {
            _cspFeatureManager.Add(new CSPFeature
            {
                Name = "STEAM_TICKET",
                Mandatory = true
            });
            await Task.Run(Initialize, stoppingToken);
        }
    }
}
#endif

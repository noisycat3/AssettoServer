using System;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Shared;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using AssettoServer.Utils;

namespace AssettoServer.Server;

internal class EntryCarClient : EntryCarBase
{
    // IEntryCar implementation
    public override IClient? Client => _client;

    private ACTcpClient? _client;
    public override bool IsAiCar => false;

    // Factory
    public delegate EntryCarClient Factory(byte inSessionId, EntryList.Entry entry);

    public EntryCarClient(byte inSessionId, EntryList.Entry entry, 
        ACServer acServer, ACServerConfiguration configuration, SessionManager sessionManager)
        : base(inSessionId, entry, acServer, configuration, sessionManager)
    {
    }

    // Internal data
    public List<ulong> AllowedGuids { get; internal set; } = new();

    // Spectator
    public bool IsSpectator => (SpectatorTargetCar != null);
    public EntryCarClient? SpectatorTargetCar { get; internal set; }

    // Ping and timings
    public override ushort Ping => _ping;
    private ushort _ping;
    public override int TimeOffset => _timeOffset;
    private int _timeOffset;
    public override string Name => (Client?.Name ?? string.Empty);

    public long? HighPingSecondsStart { get; internal set; }
    public long LastPingTime { get; internal set; }
    public long LastPongTime { get; internal set; }
    public uint LastRemoteTimestamp { get; internal set; }

    // Anti AFK
    public long LastActiveTime { get; internal set; }
    public bool HasSentAfkWarning { get; internal set; }

    // Updates
    public override bool HasUpdateToSend => _hasUpdateToSend;

    public override int InstanceCount => (_instance == null) ? 0 : 1;
    public override int InstanceMax => 1;
    public override IEnumerable<ICarInstance> Instances =>
        (_instance == null) ? Array.Empty<ICarInstance>() : _instance.Yield();

    public CarInstance? ClientCarInstance => _instance;
    private CarInstance? _instance;

    public override ICarInstance CreateInstance()
    {
        if (_instance != null)
            throw new InvalidOperationException("Client car can only have single instance!");

        _instance = new CarInstance(this);
        return _instance;
    }

    public override void DestroyInstance(ICarInstance instance)
    {
        instance.DestroyInstance();

        if (_instance == null || _instance != instance)
            throw new InvalidOperationException("Client car has no instance or instance not valid!");

        _instance = null;
    }

    private bool _hasUpdateToSend;

    // EntryCarBase implementation
    public override void UpdateCar(long currentTime)
    {
        if (_client == null || !_client.HasSentFirstUpdate)
            return;
        
        CheckAfk(_client, currentTime);
        UpdatePing(_client, currentTime);
    }

    public override ICarInstance? GetBestInstanceFor(EntryCarClient clientCar) => _instance;

    // EntryCarClient API methods
    public bool IsInRange(CarStatus targetCar, float range)
    {
        if (_instance == null)
            return false;

        return Vector3.DistanceSquared(_instance.Status.Position, targetCar.Position) < (range * range);
    }

    public async Task DisconnectClient()
    {
        if (_client != null)
            await _client.BeginDisconnectAsync();
    }

    // Additional client API methods
    internal bool AssignClient(ACTcpClient tcpClient)
    {
        if (_client != null)
        {
            Logger.Error("Trying to assign a client, but client not null! new: `{tcpClient.Name}`, old: `{_client.Name}`", 
                tcpClient.Name, _client.Name);
            return false;
        }

        if (_instance != null)
        {
            Logger.Error("Trying to assign a car instance, but client already has instance! inst: `{_instance}`",
                _instance.CarEntry.Name);
            return false;
        }

        _client = tcpClient;
        CreateInstance();
        return true;
    }

    internal bool ClearClient(ACTcpClient tcpClient, bool force = false)
    {
        if (_client != tcpClient)
        {
            Logger.Error("Trying to clear a client, but client doesn't match expectations! got: `{tcpClient.Name}`, expected: `{_client.Name}`",
                tcpClient.Name, _client?.Name ?? "<null>");

            if (!force)
                return false;
        }

        if (_instance != null)
            DestroyInstance(_instance);
        else
            Logger.Warning("Client has no car instance: `{_client.Name}`",tcpClient.Name);

        _client = null;
        return true;
    }

    internal void ResetCarState()
    {
        //IsSpectator = false;
        //SpectatorMode = 0;
        LastActiveTime = 0;
        HasSentAfkWarning = false;
        _hasUpdateToSend = false;

        //TimeOffset = 0;
        //LastRemoteTimestamp = 0;
        //HighPingSeconds = 0;
        _ping = 0;
        _timeOffset = 0;
        
        LastPingTime = 0;

        //ForceLights = false;
        //TargetCar = null;

        // Fresh status
        //Status = new CarStatus();
        _instance?.Reset();
    }

    internal void UpdateActiveTime()
    {
        LastActiveTime = SessionManager.ServerTimeMilliseconds;
        HasSentAfkWarning = false;
    }

    internal void UpdateClientPosition(in PositionUpdateIn positionUpdate)
    {
        if (_client == null)
            return;

        if (_instance == null)
        {
            _client.Logger.Debug("Client has no car instance, disconnecting",
                _client.Name, _client.SessionId);
            Server.KickAsync(_client, "Invalid position update received");
            return;
        }

        if (!positionUpdate.IsValid())
        {
            _client.Logger.Debug("Invalid position update received from {ClientName} ({SessionId}), disconnecting",
                _client.Name, _client.SessionId);
            Server.KickAsync(_client, "Invalid position update received");
            return;
        }

        _hasUpdateToSend = true;
        LastRemoteTimestamp = positionUpdate.LastRemoteTimestamp;

        // Update status
        CarStatus status = _instance.Status;

        const float afkMinSpeed = 20 / 3.6f;
        if ((positionUpdate.StatusFlag != status.StatusFlag || positionUpdate.Gas != status.Gas || positionUpdate.SteerAngle != status.SteerAngle)
            && (ServerConfiguration.Extra.AfkKickBehavior != AfkKickBehavior.MinimumSpeed || positionUpdate.Velocity.LengthSquared() > afkMinSpeed * afkMinSpeed))
        {
            UpdateActiveTime();
        }

        // Reset if falling
        if (status.Velocity.Y < -75 && _client != null)
        {
            SessionManager.SendCurrentSession(_client);
        }

        status.Timestamp = LastRemoteTimestamp + TimeOffset;
        status.PakSequenceId = positionUpdate.PakSequenceId;
        status.Position = positionUpdate.Position;
        status.Rotation = positionUpdate.Rotation;
        status.Velocity = positionUpdate.Velocity;
        status.TyreAngularSpeed[0] = positionUpdate.TyreAngularSpeedFL;
        status.TyreAngularSpeed[1] = positionUpdate.TyreAngularSpeedFR;
        status.TyreAngularSpeed[2] = positionUpdate.TyreAngularSpeedRL;
        status.TyreAngularSpeed[3] = positionUpdate.TyreAngularSpeedRR;
        status.SteerAngle = positionUpdate.SteerAngle;
        status.WheelAngle = positionUpdate.WheelAngle;
        status.EngineRpm = positionUpdate.EngineRpm;
        status.Gear = positionUpdate.Gear;
        status.StatusFlag = positionUpdate.StatusFlag;
        status.PerformanceDelta = positionUpdate.PerformanceDelta;
        status.Gas = positionUpdate.Gas;
        status.NormalizedPosition = positionUpdate.NormalizedPosition;
    }

    public override void PostUpdateCar()
    {
        _hasUpdateToSend = false;
    }

    internal void UpdateTimingValues(ushort ping, int timeOffset)
    {
        _ping = ping;
        _timeOffset = timeOffset;
    }

    // Private methods
    private void CheckAfk(ACTcpClient client, long currentTime)
    {
        if (!ServerConfiguration.Extra.EnableAntiAfk || client.IsAdministrator)
            return;

        long timeAfk = currentTime - LastActiveTime;
        if (timeAfk > ServerConfiguration.Extra.MaxAfkTimeMilliseconds)
            _ = Task.Run(() => Server.KickAsync(client, "being AFK"));
        else if (!HasSentAfkWarning && ServerConfiguration.Extra.MaxAfkTimeMilliseconds - timeAfk < 60000)
        {
            HasSentAfkWarning = true;
            client.SendPacket(new ChatMessage { SessionId = 255, Message = "You will be kicked in 1 minute for being AFK." });
        }
    }

    private void UpdatePing(ACTcpClient client, long currentTime)
    {
        if ((currentTime - LastPingTime) > 1000)
        {
            LastPingTime = currentTime;
            client.SendPacketUdp(new PingUpdate((uint)LastPingTime, Ping));

            if (currentTime - LastPongTime > 15000)
            {
                client.Logger.Information("{ClientName} has not sent a ping response for over 15 seconds", client.Name);
                _ = client.BeginDisconnectAsync();
            }
        }
    }
}



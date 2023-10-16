using AssettoServer.Network.Tcp;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Shared;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

namespace AssettoServer.Server;

public class EntryCarClient : EntryCarBase
{
    // IEntryCar implementation
    public override IClient? Client => _client;

    private ACTcpClient? _client = null;
    public override bool IsAiCar => false;

    // Factory
    public delegate EntryCarClient Factory(byte sessionId);

    public EntryCarClient( byte inSessionId, ACServer acServer, ACServerConfiguration configuration, EntryCarManager entryCarManager, SessionManager sessionManager)
        : base(inSessionId, acServer, configuration, entryCarManager, sessionManager)
    {
    }

    // Internal data
    public CarStatus Status { get; private set; } = new();
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
    public bool HasUpdateToSend { get; internal set; }

    // EntryCarBase implementation
    public override void UpdateCar()
    {
        if (_client == null || !_client.HasSentFirstUpdate)
            return;
        
        CheckAfk(_client);
        UpdatePing(_client);
    }

    public override CarStatus? GetPositionUpdateForClient(EntryCarClient clientCar)
    {
        // Just return the status if client is connected
        return (_client != null) ? Status : null;
    }

    // EntryCarClient API methods
    public bool IsInRange(CarStatus targetCar, float range)
    {
        return Vector3.DistanceSquared(Status.Position, targetCar.Position) < (range * range);
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

        _client = tcpClient;
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

        _client = null;
        return true;
    }

    internal void ResetCarState()
    {
        //IsSpectator = false;
        //SpectatorMode = 0;
        LastActiveTime = 0;
        HasSentAfkWarning = false;
        HasUpdateToSend = false;

        //TimeOffset = 0;
        //LastRemoteTimestamp = 0;
        //HighPingSeconds = 0;
        _ping = 0;
        _timeOffset = 0;
        
        LastPingTime = 0;

        //ForceLights = false;
        //TargetCar = null;

        // Fresh status
        Status = new CarStatus();
    }

    internal void UpdateActiveTime()
    {
        LastActiveTime = _sessionManager.ServerTimeMilliseconds;
        HasSentAfkWarning = false;
    }

    internal void UpdateClientPosition(in PositionUpdateIn positionUpdate)
    {
        if (!positionUpdate.IsValid())
        {
            if (_client == null) 
                return;
            _client.Logger.Debug("Invalid position update received from {ClientName} ({SessionId}), disconnecting",
                _client.Name, _client.SessionId);
            _ = _client.BeginDisconnectAsync();
            return;
        }
        
        HasUpdateToSend = true;
        LastRemoteTimestamp = positionUpdate.LastRemoteTimestamp;

        const float afkMinSpeed = 20 / 3.6f;
        if ((positionUpdate.StatusFlag != Status.StatusFlag || positionUpdate.Gas != Status.Gas || positionUpdate.SteerAngle != Status.SteerAngle)
            && (_configuration.Extra.AfkKickBehavior != AfkKickBehavior.MinimumSpeed || positionUpdate.Velocity.LengthSquared() > afkMinSpeed * afkMinSpeed))
        {
            UpdateActiveTime();
        }

        // Reset if falling
        if (Status.Velocity.Y < -75 && _client != null)
        {
            _sessionManager.SendCurrentSession(_client);
        }

        Status.Timestamp = LastRemoteTimestamp + TimeOffset;
        Status.PakSequenceId = positionUpdate.PakSequenceId;
        Status.Position = positionUpdate.Position;
        Status.Rotation = positionUpdate.Rotation;
        Status.Velocity = positionUpdate.Velocity;
        Status.TyreAngularSpeed[0] = positionUpdate.TyreAngularSpeedFL;
        Status.TyreAngularSpeed[1] = positionUpdate.TyreAngularSpeedFR;
        Status.TyreAngularSpeed[2] = positionUpdate.TyreAngularSpeedRL;
        Status.TyreAngularSpeed[3] = positionUpdate.TyreAngularSpeedRR;
        Status.SteerAngle = positionUpdate.SteerAngle;
        Status.WheelAngle = positionUpdate.WheelAngle;
        Status.EngineRpm = positionUpdate.EngineRpm;
        Status.Gear = positionUpdate.Gear;
        Status.StatusFlag = positionUpdate.StatusFlag;
        Status.PerformanceDelta = positionUpdate.PerformanceDelta;
        Status.Gas = positionUpdate.Gas;
        Status.NormalizedPosition = positionUpdate.NormalizedPosition;
    }

    internal void UpdateTimingValues(ushort ping, int timeOffset)
    {
        _ping = ping;
        _timeOffset = timeOffset;
    }

    // Private methods
    private void CheckAfk(ACTcpClient client)
    {
        if (!_configuration.Extra.EnableAntiAfk || client.IsAdministrator)
            return;

        long timeAfk = _sessionManager.ServerTimeMilliseconds - LastActiveTime;
        if (timeAfk > _configuration.Extra.MaxAfkTimeMilliseconds)
            _ = Task.Run(() => _acServer.KickAsync(client, "being AFK"));
        else if (!HasSentAfkWarning && _configuration.Extra.MaxAfkTimeMilliseconds - timeAfk < 60000)
        {
            HasSentAfkWarning = true;
            client.SendPacket(new ChatMessage { SessionId = 255, Message = "You will be kicked in 1 minute for being AFK." });
        }
    }

    private void UpdatePing(ACTcpClient client)
    {
        if ((_sessionManager.ServerTimeMilliseconds - LastPingTime) > 1000)
        {
            LastPingTime = _sessionManager.ServerTimeMilliseconds;
            client.SendPacketUdp(new PingUpdate((uint)LastPingTime, Ping));

            if (_sessionManager.ServerTimeMilliseconds - LastPongTime > 15000)
            {
                client.Logger.Information("{ClientName} has not sent a ping response for over 15 seconds", client.Name);
                _ = client.BeginDisconnectAsync();
            }
        }
    }
}



using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AssettoServer.Commands;
using AssettoServer.Network.Udp;
using AssettoServer.Server;
using AssettoServer.Server.Blacklist;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.OpenSlotFilters;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Shared.Network.Packets.Outgoing.Handshake;
using AssettoServer.Shared.Network.Packets.Shared;
using AssettoServer.Shared.Weather;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace AssettoServer.Network.Tcp;

public class ACTcpClient : IClient
{
    private ACServer Server => _acServer;
    private ACUdpServer UdpServer { get; }
    public ILogger Logger { get; }

    public byte SessionId { get; set; }
    public bool InGame => (HasSentFirstUpdate && !IsDisconnectRequested);
    public bool IsUdpReady => (UdpEndpoint != null);
    public bool IsDisconnectRequested => (_disconnectRequested == 1);

    public string? Name { get; set; }
    public string? Team { get; private set; }
    public string? NationCode { get; private set; }

    public bool IsAdministrator { get; internal set; }
    public ulong Guid { get; internal set; }
    public string HashedGuid { get; private set; } = "";
    public ulong? OwnerGuid { get; internal set; }
    public EntryCarClient? ClientCar { get; internal set; }

    [MemberNotNullWhen(true, nameof(Name), nameof(Team), nameof(NationCode))]
    public bool HasSentFirstUpdate { get; private set; }
    public bool HasReceivedFirstPositionUpdate { get; private set; }
    public bool IsConnected { get; set; }
    public TcpClient TcpClient { get; }

    private NetworkStream TcpStream { get; }
    [MemberNotNullWhen(true, nameof(Name), nameof(Team), nameof(NationCode))]
    public bool HasStartedHandshake { get; private set; }
    public bool HasPassedChecksum { get; private set; }
    public int SecurityLevel { get; set; }
    public ulong? HardwareIdentifier { get; set; }
    public InputMethod InputMethod { get; set; }

    internal SocketAddress? UdpEndpoint { get; private set; }
    internal bool SupportsCSPCustomUpdate { get; private set; }
    internal string ApiKey { get; }

    private static ThreadLocal<byte[]> UdpSendBuffer { get; } = new(() => GC.AllocateArray<byte>(1500, true));
    private Memory<byte> TcpSendBuffer { get; }
    private Channel<IOutgoingNetworkPacket> OutgoingPacketChannel { get; }
    private CancellationTokenSource DisconnectTokenSource { get; }
    private Task SendLoopTask { get; set; } = null!;
    private long LastChatTime { get; set; }
    private int _disconnectRequested;
    
    private readonly ACServer _acServer;
    private readonly WeatherManager _weatherManager;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly ACServerConfiguration _configuration;
    private readonly IBlacklistService _blacklist;
    private readonly ChecksumManager _checksumManager;
    private readonly CSPFeatureManager _cspFeatureManager;
    private readonly CSPServerExtraOptions _cspServerExtraOptions;
    private readonly OpenSlotFilterChain _openSlotFilter;
    private readonly CSPClientMessageHandler _clientMessageHandler;
    private readonly ChatService _chatService;

    public class ACTcpClientLogEventEnricher : ILogEventEnricher
    {
        private readonly ACTcpClient _client;

        public ACTcpClientLogEventEnricher(ACTcpClient client)
        {
            _client = client;
        }
            
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var endpoint = (IPEndPoint)_client.TcpClient.Client.RemoteEndPoint!;
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ClientName", _client.Name));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ClientSteamId", _client.Guid));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ClientIpAddress", endpoint.Address.ToString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ClientPort", endpoint.Port));
            if (_client.HardwareIdentifier.HasValue)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ClientHWID", _client.HardwareIdentifier.Value));
            }
        }
    }

    public ACTcpClient(
        ACServer acServer,
        ACUdpServer udpServer, 
        TcpClient tcpClient,
        SessionManager sessionManager,
        WeatherManager weatherManager,
        ACServerConfiguration configuration,
        EntryCarManager entryCarManager,
        IBlacklistService blacklist,
        ChecksumManager checksumManager,
        CSPFeatureManager cspFeatureManager,
        CSPServerExtraOptions cspServerExtraOptions,
        OpenSlotFilterChain openSlotFilter, 
        CSPClientMessageHandler clientMessageHandler,
        ChatService chatService)
    {
        _acServer = acServer;

        UdpServer = udpServer;
        Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.With(new ACTcpClientLogEventEnricher(this))
            .WriteTo.Logger(Log.Logger)
            .CreateLogger();

        TcpClient = tcpClient;
        _sessionManager = sessionManager;
        _weatherManager = weatherManager;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _blacklist = blacklist;
        _checksumManager = checksumManager;
        _cspFeatureManager = cspFeatureManager;
        _cspServerExtraOptions = cspServerExtraOptions;
        _openSlotFilter = openSlotFilter;
        _clientMessageHandler = clientMessageHandler;
        _chatService = chatService;

        tcpClient.ReceiveTimeout = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
        tcpClient.SendTimeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds;
        tcpClient.LingerState = new LingerOption(true, 2);

        TcpStream = tcpClient.GetStream();

        TcpSendBuffer = new byte[8192 + (_cspServerExtraOptions.EncodedWelcomeMessage.Length * 4) + 2];
        OutgoingPacketChannel = Channel.CreateBounded<IOutgoingNetworkPacket>(512);
        DisconnectTokenSource = new CancellationTokenSource();

        ApiKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    }

    internal Task StartAsync()
    {
        SendLoopTask = Task.Run(SendLoopAsync);
        _ = Task.Run(ReceiveLoopAsync);

        return Task.CompletedTask;
    }

    public void SendPacket<TPacket>(TPacket packet) where TPacket : IOutgoingNetworkPacket
    {
        try
        {
            if (!OutgoingPacketChannel.Writer.TryWrite(packet) && !(packet is SunAngleUpdate) && !IsDisconnectRequested)
            {
                Logger.Warning("Cannot write packet to TCP packet queue for {ClientName}, disconnecting", Name);
                _ = Server.KickAsync(this, "TCP send error");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error sending {PacketName} to {ClientName}", typeof(TPacket).Name, Name);
        }
    }

    public void SendPacketUdp<TPacket>(in TPacket packet) where TPacket : IOutgoingNetworkPacket
    {
        if (UdpEndpoint == null) return;

        try
        {
            byte[] buffer = UdpSendBuffer.Value!;
            PacketWriter writer = new PacketWriter(buffer);
            int bytesWritten = writer.WritePacket(in packet);

            //Logger.Information("Sent UDP packet {packet.GetType().Name}, len: {bytesWritten}", 
            //    packet.GetType().Name, bytesWritten);

            UdpServer.Send(UdpEndpoint, buffer, 0, bytesWritten);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error sending {PacketName} to {ClientName}", typeof(TPacket).Name, Name);
            _ = Server.KickAsync(this, "UDP send error");
        }
    }

    public event EventHandler<IClient, HandshakeAcceptedEventArgs>? HandshakeAccepted;
    public event EventHandler<IClient, ClientChecksumResultEventArgs>? ChecksumCompleted;
    public event EventHandler<IClient, EventArgs>? GameLoaded;
    public event EventHandler<IClient, EventArgs>? Disconnected;
    public event EventHandler<IClient, ChatMessageEventArgs>? ChatMessageEvent;
    public event EventHandler<IClient, ChatMessageEventArgs>? ChatRawEvent;
    public event EventHandler<IClient, LapCompletedEventArgs>? LapCompleted;
    
    private async Task SendLoopAsync()
    {
        try
        {
            await foreach (var packet in OutgoingPacketChannel.Reader.ReadAllAsync(DisconnectTokenSource.Token))
            {
                if (packet is not SunAngleUpdate)
                {
                    if (packet is AuthFailedResponse authResponse)
                        Logger.Debug("Sending {PacketName} ({AuthResponseReason})", packet.GetType().Name, authResponse.Reason);
                    else if (packet is ChatMessage { SessionId: 255 } chatMessage)
                        Logger.Verbose("Sending {PacketName} ({ChatMessage}) to {ClientName}", packet.GetType().Name, chatMessage.Message, Name);
                    else
                        Logger.Verbose("Sending {PacketName} to {ClientName}", packet.GetType().Name, Name);
                }

                PacketWriter writer = new PacketWriter(TcpStream, TcpSendBuffer);
                writer.WritePacket(packet);

                await writer.SendAsync(DisconnectTokenSource.Token);
            }
        }
        catch (ChannelClosedException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error sending TCP packet to {ClientName}", Name);
            _ = Server.KickAsync(this, "Error sending TCP packet");
        }
    }

    private async Task ReceiveLoopAsync()
    {
        byte[] buffer = new byte[2046];
        NetworkStream stream = TcpStream;
        
        try
        {
            while (!DisconnectTokenSource.IsCancellationRequested)
            {
                PacketReader reader = new PacketReader(stream, buffer);
                reader.SliceBuffer(await reader.ReadPacketAsync());

                if (reader.Buffer.Length == 0)
                    return;

                ACServerProtocol id = (ACServerProtocol)reader.Read<byte>();

                if (id != ACServerProtocol.ClientEvent)
                    Logger.Verbose("Received TCP packet with ID {PacketId:X}", id);

                if (!HasStartedHandshake && id != ACServerProtocol.RequestNewConnection)
                    return;

                if (!HasStartedHandshake)
                {
                    HandshakeRequest handshakeRequest = reader.ReadPacket<HandshakeRequest>();
                    if (handshakeRequest.Name.Length > 25)
                        handshakeRequest.Name = handshakeRequest.Name.Substring(0, 25);

                    Name = handshakeRequest.Name.Trim();
                    Team = handshakeRequest.Team;
                    NationCode = handshakeRequest.Nation;
                    Guid = handshakeRequest.Guid;
                    HashedGuid = IdFromGuid(Guid);

                    Logger.Information("{ClientName} ({ClientSteamId} - {ClientIpEndpoint}) is attempting to connect ({CarModel})", handshakeRequest.Name, handshakeRequest.Guid, TcpClient.Client.RemoteEndPoint?.ToString(), handshakeRequest.RequestedCar);

                    List<string> cspFeatures;
                    if (!string.IsNullOrEmpty(handshakeRequest.Features))
                    {
                        cspFeatures = handshakeRequest.Features.Split(',').ToList();
                        Logger.Debug("{ClientName} supports extra CSP features: {ClientFeatures}", handshakeRequest.Name, cspFeatures);
                    }
                    else
                    {
                        cspFeatures = new List<string>();
                    }

                    AuthFailedResponse? response;
                    if (id != ACServerProtocol.RequestNewConnection || handshakeRequest.ClientVersion != 202)
                        SendPacket(new UnsupportedProtocolResponse());
                    else if (await _blacklist.IsBlacklistedAsync(handshakeRequest.Guid))
                        SendPacket(new BlacklistedResponse());
                    else if (_configuration.Server.Password?.Length > 0 && handshakeRequest.Password != _configuration.Server.Password && handshakeRequest.Password != _configuration.Server.AdminPassword)
                        SendPacket(new WrongPasswordResponse());
                    else if (!_sessionManager.CurrentSession.Configuration.IsOpen)
                        SendPacket(new SessionClosedResponse());
                    else if (Name.Length == 0)
                        SendPacket(new AuthFailedResponse("Driver name cannot be empty."));
                    else if (!_cspFeatureManager.ValidateHandshake(cspFeatures))
                        SendPacket(new AuthFailedResponse("Missing CSP features. Please update CSP and/or Content Manager."));
                    else if ((response = await _openSlotFilter.ShouldAcceptConnectionAsync(this, handshakeRequest)).HasValue)
                        SendPacket(response.Value);
                    else if (!await _entryCarManager.TrySecureSlotAsync(this, handshakeRequest.RequestedCar))
                        SendPacket(new NoSlotsAvailableResponse());
                    else
                    {
                        if (ClientCar == null)
                            throw new InvalidOperationException("No EntryCar set even though handshake started");

                        ClientCar.UpdateActiveTime();
                        SupportsCSPCustomUpdate = _configuration.Extra.EnableCustomUpdate && cspFeatures.Contains("CUSTOM_UPDATE");

                        // OLD AI: Despawn AI instances when someone connects
                        // Gracefully despawn AI cars
                        //EntryCar.SetAiOverbooking(0);

                        if (handshakeRequest.Password == _configuration.Server.AdminPassword)
                            IsAdministrator = true;

                        // From this point onward, the client is considered to be connected.
                        IsConnected = true;
                        _acServer.NotifyClientConnected(this);
                        Logger.Information("{ClientName} ({ClientSteamId}, {SessionId} ({CarModel}-{CarSkin})) has connected", 
                            Name, Guid, SessionId, ClientCar.Model, ClientCar.Skin);

                        var cfg = _configuration.Server;
                        HandshakeResponse handshakeResponse = new HandshakeResponse
                        {
                            ABSAllowed = cfg.ABSAllowed,
                            TractionControlAllowed = cfg.TractionControlAllowed,
                            AllowedTyresOutCount = cfg.AllowedTyresOutCount,
                            AllowTyreBlankets = cfg.AllowTyreBlankets,
                            AutoClutchAllowed = cfg.AutoClutchAllowed,
                            CarModel = ClientCar.Model,
                            CarSkin = ClientCar.Skin,
                            FuelConsumptionRate = cfg.FuelConsumptionRate,
                            HasExtraLap = cfg.HasExtraLap,
                            InvertedGridPositions = cfg.InvertedGridPositions,
                            IsGasPenaltyDisabled = cfg.IsGasPenaltyDisabled,
                            IsVirtualMirrorForced = cfg.IsVirtualMirrorForced,
                            JumpStartPenaltyMode = cfg.JumpStartPenaltyMode,
                            MechanicalDamageRate = cfg.MechanicalDamageRate,
                            PitWindowEnd = cfg.PitWindowEnd,
                            PitWindowStart = cfg.PitWindowStart,
                            StabilityAllowed = cfg.StabilityAllowed,
                            RaceOverTime = cfg.RaceOverTime,
                            RefreshRateHz = cfg.RefreshRateHz,
                            ResultScreenTime = cfg.ResultScreenTime,
                            ServerName = cfg.Name,
                            SessionId = SessionId,
                            SunAngle = (float)WeatherUtils.SunAngleFromTicks(_weatherManager.CurrentDateTime.TimeOfDay.TickOfDay),
                            TrackConfig = cfg.TrackConfig,
                            TrackName = cfg.Track,
                            TyreConsumptionRate = cfg.TyreConsumptionRate,
                            UdpPort = cfg.UdpPort,
                            CurrentSession = _sessionManager.CurrentSession.Configuration,
                            SessionTime = _sessionManager.CurrentSession.SessionTimeMilliseconds,
                            ChecksumCount = (byte)_checksumManager.TrackChecksums.Count,
                            ChecksumPaths = _checksumManager.TrackChecksums.Keys,
                            CurrentTime = 0, // Ignored by AC
                            LegalTyres = cfg.LegalTyres,
                            RandomSeed = 123,
                            SessionCount = (byte)_configuration.Sessions.Count,
                            Sessions = _configuration.Sessions,
                            SpawnPosition = SessionId,
                            TrackGrip = _weatherManager.CurrentWeather.TrackGrip,
                            MaxContactsPerKm = cfg.MaxContactsPerKm
                        };

                        HandshakeAccepted?.Invoke(this, new HandshakeAcceptedEventArgs()
                        {
                            HandshakeResponse = handshakeResponse
                        });

                        HasStartedHandshake = true;
                        SendPacket(handshakeResponse);

                        _ = Task.Delay(TimeSpan.FromMinutes(_configuration.Extra.PlayerLoadingTimeoutMinutes)).ContinueWith(async _ =>
                        {
                            if (!IsDisconnectRequested && (ClientCar != null) && (ClientCar.Client == this) && IsConnected && !HasSentFirstUpdate)
                            {
                                Logger.Information("{ClientName} has taken too long to spawn in and will be disconnected", Name);
                                await BeginDisconnectAsync();
                            }
                        });
                    }

                    if (!HasStartedHandshake)
                        return;
                }
                else if (HasStartedHandshake)
                {
                    switch (id)
                    {
                        case ACServerProtocol.CleanExitDrive:
                            Logger.Debug("Received clean exit from {ClientName} ({SessionId})", Name, SessionId);
                            return;
                        case ACServerProtocol.P2PUpdate:
                            OnP2PUpdate(reader);
                            break;
                        case ACServerProtocol.CarListRequest:
                            OnCarListRequest(reader);
                            break;
                        case ACServerProtocol.Checksum:
                            OnChecksum(reader);
                            break;
                        case ACServerProtocol.Chat:
                            OnChat(reader);
                            break;
                        case ACServerProtocol.DamageUpdate:
                            OnDamageUpdate(reader);
                            break;
                        case ACServerProtocol.LapCompleted:
                            OnLapCompletedMessageReceived(reader);
                            break;
                        case ACServerProtocol.TyreCompoundChange:
                            OnTyreCompoundChange(reader);
                            break;
                        case ACServerProtocol.ClientEvent:
                            OnClientEvent(reader);
                            break;
                        case ACServerProtocol.Extended:
                            byte extendedId = reader.Read<byte>();
                            Logger.Verbose("Received extended TCP packet with ID {PacketId:X}", id);

                            if (extendedId == (byte)CSPMessageTypeTcp.SpectateCar)
                                OnSpectateCar(reader);
                            else if (extendedId == (byte)CSPMessageTypeTcp.ClientMessage)
                                _clientMessageHandler.OnCSPClientMessageTcp(this, reader);
                            break;
                    }
                }
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error receiving TCP packet from {ClientName}", Name);
        }
        finally
        {
            await BeginDisconnectAsync();
        }
    }

    private void OnClientEvent(PacketReader reader)
    {
        using var clientEvent = reader.ReadPacket<ClientEvent>();

        //foreach (var evt in clientEvent.ClientEvents)
        //{
        //    EntryCarBase? targetCar = null;
                
        //    if (evt.Type == ClientEventType.CollisionWithCar)
        //    {
        //        targetCar = _entryCarManager.EntryCars[evt.TargetSessionId];
        //        Logger.Information("Collision between {SourceCarName} ({SourceCarSessionId}) and {TargetCarName} ({TargetCarSessionId}), rel. speed {Speed:F0}km/h", 
        //            Name, EntryCar.SessionId, targetCar.Client?.Name ?? targetCar.AiName, targetCar.SessionId, evt.Speed);
        //    }
        //    else
        //    {
        //        Logger.Information("Collision between {SourceCarName} ({SourceCarSessionId}) and environment, rel. speed {Speed:F0}km/h", 
        //            Name, EntryCar.SessionId, evt.Speed);
        //    }
        //}
    }

    private void OnSpectateCar(PacketReader reader)
    {
        SpectateCar spectatePacket = reader.ReadPacket<SpectateCar>();

        if (ClientCar == null)
            return;

        if (_entryCarManager.EntryCars[spectatePacket.SessionId] is EntryCarClient targetClientCar)
            ClientCar.SpectatorTargetCar = targetClientCar;
        else
            ClientCar.SpectatorTargetCar = null;
    }

    private void OnChecksum(PacketReader reader)
    {
        bool passedChecksum = false;

        if (ClientCar != null)
        {
            byte[] fullChecksum = new byte[16 * (_checksumManager.TrackChecksums.Count + 1)];
            if (reader.Buffer.Length == fullChecksum.Length + 1)
            {
                reader.ReadBytes(fullChecksum);
                passedChecksum =
                    !_checksumManager.CarChecksums.TryGetValue(ClientCar.Model, out List<byte[]>? modelChecksums) ||
                    modelChecksums.Count == 0
                    || modelChecksums.Any(c => fullChecksum.AsSpan().Slice(fullChecksum.Length - 16).SequenceEqual(c));

                KeyValuePair<string, byte[]>[] allChecksums = _checksumManager.TrackChecksums.ToArray();
                for (int i = 0; i < allChecksums.Length; i++)
                {
                    if (!allChecksums[i].Value.AsSpan().SequenceEqual(fullChecksum.AsSpan().Slice(i * 16, 16)))
                    {
                        Logger.Information("{ClientName} failed checksum for file {ChecksumFile}", Name,
                            allChecksums[i].Key);
                        passedChecksum = false;
                        break;
                    }
                }
            }
        }
        else
        {
            Logger.Warning("Client is required to perform a checksum, but has no car! {this}", this);
        }

        // Checksum callback
        ClientChecksumResultEventArgs eventArgs = new()
        {
            ChecksumValid = passedChecksum,
        };
        ChecksumCompleted?.Invoke(this, eventArgs);
        HasPassedChecksum = eventArgs.ChecksumValid;

        // Kick unverified, otherwise broadcast the connection
        if (HasPassedChecksum)
        {
            _acServer.BroadcastPacket(new CarConnected
            {
                SessionId = SessionId,
                Name = Name,
                Nation = NationCode
            }, this);
        }
        else
        {
            _ = _acServer.KickAsync(this, KickReason.ChecksumFailed, null, null,
                $"{Name} failed the checksum check and has been kicked.");
        }
    }

    private void OnChat(PacketReader reader)
    {
        long currentTime = _sessionManager.ServerTimeMilliseconds;
        if (currentTime - LastChatTime < 1000)
            return;
        LastChatTime = currentTime;

        if (_configuration.Extra.AfkKickBehavior == AfkKickBehavior.PlayerInput)
        {
            ClientCar?.UpdateActiveTime();
        }

        // Read chat
        ChatMessage chatMessage = reader.ReadPacket<ChatMessage>();
        chatMessage.SessionId = SessionId;

        Logger.Information("CHAT MESSAGE: {ClientName} ({SessionId}): {ChatMessage}", Name, SessionId, chatMessage.Message);

        // Prepare chat event
        ChatMessageEventArgs eventArgs = new ChatMessageEventArgs() {
            ChatMessage = chatMessage
        };

        // Process event before the command. Early return if done.
        ChatRawEvent?.Invoke(this, eventArgs);
        if (eventArgs.Cancel)
            return;

        // Try Normal command execution
        if (_chatService.ProcessChatCommand(this, chatMessage))
            return;

        // Process event before the command. Early return if done.
        ChatMessageEvent?.Invoke(this, eventArgs);
        if (eventArgs.Cancel)
            return;

        // Finally, let's broadcast
        _acServer.BroadcastPacket(eventArgs.ChatMessage);
    }

    private void OnDamageUpdate(PacketReader reader)
    {
        DamageUpdateIncoming damageUpdate = reader.ReadPacket<DamageUpdateIncoming>();

        if (ClientCar != null)
            ClientCar.Status.DamageZoneLevel = damageUpdate.DamageZoneLevel;
        else
            Logger.Warning("OnDamageUpdate: Client has no car!");
            
        _acServer.BroadcastPacket(new DamageUpdate
        {
            SessionId = SessionId,
            DamageZoneLevel = damageUpdate.DamageZoneLevel,
        }, this);
    }

    private void OnTyreCompoundChange(PacketReader reader)
    {
        TyreCompoundChangeRequest compoundChangeRequest = reader.ReadPacket<TyreCompoundChangeRequest>();
        if (ClientCar != null)
            ClientCar.Status.CurrentTyreCompound = compoundChangeRequest.CompoundName;
        else
            Logger.Warning("OnTyreCompoundChange: Client has no car!");

        _acServer.BroadcastPacket(new TyreCompoundUpdate
        {
            CompoundName = compoundChangeRequest.CompoundName,
            SessionId = SessionId
        });
    }

    private void OnP2PUpdate(PacketReader reader)
    {
        // ReSharper disable once InconsistentNaming
        P2PUpdateRequest p2pUpdateRequest = reader.ReadPacket<P2PUpdateRequest>();
        if (ClientCar == null)
        {
            Logger.Warning("OnP2PUpdate: Client has no car!");
            return;
        }

        if (p2pUpdateRequest.P2PCount == -1)
        {
            SendPacket(new P2PUpdate
            {
                Active = false,
                P2PCount = ClientCar.Status.P2PCount,
                SessionId = SessionId
            });
        }
        else
        {
            _acServer.BroadcastPacket(new P2PUpdate
            {
                Active = ClientCar.Status.P2PActive,
                P2PCount = ClientCar.Status.P2PCount,
                SessionId = SessionId
            });
        }
    }

    private void OnCarListRequest(PacketReader reader)
    {
        CarListRequest carListRequest = reader.ReadPacket<CarListRequest>();

        List<CarListResponse.Entry> carsInPage = _entryCarManager.ClientCars
            .Skip(carListRequest.PageIndex).Take(10)
            .Select(e => new CarListResponse.Entry()
            {
                SessionId = e.SessionId,
                Model = e.Model,
                Skin = e.Skin,
                ClientName = e.Client?.Name ?? "<unknown>",
                TeamName = e.Client?.Team ?? "<unknown>",
                NationCode = e.Client?.NationCode ?? "<unknown>",
                IsSpectator = e.IsSpectator,
                DamageZoneLevel = e.Status.DamageZoneLevel.ToArray(),
            }).ToList();

        CarListResponse carListResponse = new CarListResponse
        {
            PageIndex = carListRequest.PageIndex,
            EntryCarsCount = carsInPage.Count,
            Entries = carsInPage
        };

        SendPacket(carListResponse);
    }

    private void OnLapCompletedMessageReceived(PacketReader reader)
    {
        LapCompletedIncoming lapPacket = reader.ReadPacket<LapCompletedIncoming>();

        //_configuration.DynamicTrack.TotalLapCount++; // TODO reset at some point
        if (OnLapCompleted(lapPacket))
        {
            LapCompletedOutgoing packet = CreateLapCompletedPacket(SessionId, lapPacket.LapTime, lapPacket.Cuts);
            _acServer.BroadcastPacket(packet);
            LapCompleted?.Invoke(this, new LapCompletedEventArgs(packet));
        }
    }

    private bool OnLapCompleted(LapCompletedIncoming lap)
    {
        int timestamp = (int)_sessionManager.ServerTimeMilliseconds;

        EntryCarResult entryCarResult = _sessionManager.CurrentSession.Results?[SessionId] ?? 
                                        throw new InvalidOperationException("Current session does not have results set");

        if (entryCarResult.HasCompletedLastLap)
        {
            Logger.Debug("Lap rejected by {ClientName}, already finished", Name);
            return false;
        }

        if (_sessionManager.CurrentSession.Configuration.Type == SessionType.Race && entryCarResult.NumLaps >= _sessionManager.CurrentSession.Configuration.Laps && !_sessionManager.CurrentSession.Configuration.IsTimedRace)
        {
            Logger.Debug("Lap rejected by {ClientName}, race over", Name);
            return false;
        }

        Logger.Information("Lap completed by {ClientName}, {NumCuts} cuts, laptime {LapTime}", Name, lap.Cuts, lap.LapTime);

        // TODO unfuck all of this

        if (_sessionManager.CurrentSession.Configuration.Type == SessionType.Race || lap.Cuts == 0)
        {
            entryCarResult.LastLap = lap.LapTime;
            if (lap.LapTime < entryCarResult.BestLap)
            {
                entryCarResult.BestLap = lap.LapTime;
            }

            entryCarResult.NumLaps++;
            if (entryCarResult.NumLaps > _sessionManager.CurrentSession.LeaderLapCount)
            {
                _sessionManager.CurrentSession.LeaderLapCount = entryCarResult.NumLaps;
            }

            entryCarResult.TotalTime = (uint)(_sessionManager.CurrentSession.SessionTimeMilliseconds - ClientCar!.Ping / 2);

            if (_sessionManager.CurrentSession.SessionOverFlag)
            {
                if (_sessionManager.CurrentSession.Configuration is { Type: SessionType.Race, IsTimedRace: true })
                {
                    if (_configuration.Server.HasExtraLap)
                    {
                        if (entryCarResult.NumLaps <= _sessionManager.CurrentSession.LeaderLapCount)
                        {
                            entryCarResult.HasCompletedLastLap = _sessionManager.CurrentSession.LeaderHasCompletedLastLap;
                        }
                        else if (_sessionManager.CurrentSession.TargetLap > 0)
                        {
                            if (entryCarResult.NumLaps >= _sessionManager.CurrentSession.TargetLap)
                            {
                                _sessionManager.CurrentSession.LeaderHasCompletedLastLap = true;
                                entryCarResult.HasCompletedLastLap = true;
                            }
                        }
                        else
                        {
                            _sessionManager.CurrentSession.TargetLap = entryCarResult.NumLaps + 1;
                        }
                    }
                    else if (entryCarResult.NumLaps <= _sessionManager.CurrentSession.LeaderLapCount)
                    {
                        entryCarResult.HasCompletedLastLap = _sessionManager.CurrentSession.LeaderHasCompletedLastLap;
                    }
                    else
                    {
                        _sessionManager.CurrentSession.LeaderHasCompletedLastLap = true;
                        entryCarResult.HasCompletedLastLap = true;
                    }
                }
                else
                {
                    entryCarResult.HasCompletedLastLap = true;
                }
            }

            if (_sessionManager.CurrentSession.Configuration.Type != SessionType.Race)
            {
                if (_sessionManager.CurrentSession.EndTime != 0)
                {
                    entryCarResult.HasCompletedLastLap = true;
                }
            }
            else if (_sessionManager.CurrentSession.Configuration.IsTimedRace)
            {
                if (_sessionManager.CurrentSession is { LeaderHasCompletedLastLap: true, EndTime: 0 })
                {
                    _sessionManager.CurrentSession.EndTime = timestamp;
                }
            }
            else if (entryCarResult.NumLaps != _sessionManager.CurrentSession.Configuration.Laps)
            {
                if (_sessionManager.CurrentSession.EndTime != 0)
                {
                    entryCarResult.HasCompletedLastLap = true;
                }
            }
            else if (!entryCarResult.HasCompletedLastLap)
            {
                entryCarResult.HasCompletedLastLap = true;
                if (_sessionManager.CurrentSession.EndTime == 0)
                {
                    _sessionManager.CurrentSession.EndTime = timestamp;
                }
            }
            else if (_sessionManager.CurrentSession.EndTime != 0)
            {
                entryCarResult.HasCompletedLastLap = true;
            }

            return true;
        }

        if (_sessionManager.CurrentSession.EndTime == 0)
            return true;

        entryCarResult.HasCompletedLastLap = true;
        return false;
    }

    internal void ReceivedFirstPositionUpdate()
    {
        if (HasReceivedFirstPositionUpdate)
            return;

        HasReceivedFirstPositionUpdate = true;
        
        _ = Task.Delay(_configuration.Extra.MaxChecksumWaitTime * 1000).ContinueWith(async _ =>
        {
            if (!HasPassedChecksum && IsConnected)
            {
                await _acServer.KickAsync(this, KickReason.ChecksumFailed, null, null, 
                    $"{Name} did not send the requested checksums.");
            }
        });
    }

    internal void SendFirstUpdate()
    {
        if (HasSentFirstUpdate || ClientCar == null)
            return;

        TcpClient.ReceiveTimeout = 0;
        ClientCar.LastPongTime = _sessionManager.ServerTimeMilliseconds;

        // Filter all cars with connected client and all AI cars
        List<EntryCarBase> connectedCars = _entryCarManager.EntryCars
            .Where(c => (c.Client != null || c.IsAiCar)).ToList();

        if (!string.IsNullOrEmpty(_cspServerExtraOptions.EncodedWelcomeMessage))
            SendPacket(new WelcomeMessage { Message = _cspServerExtraOptions.EncodedWelcomeMessage });

        SendPacket(new DriverInfoUpdate { ConnectedCars = connectedCars });
        _weatherManager.SendWeather(this);

        foreach (EntryCarBase car in connectedCars)
        {
            SendPacket(new MandatoryPitUpdate { MandatoryPit = false, SessionId = car.SessionId });
        }

        // Update tires on all client cars
        foreach (EntryCarClient otherClientCar in _entryCarManager.ClientCars)
        {
            if (otherClientCar != ClientCar)
                SendPacket(new TyreCompoundUpdate { SessionId = otherClientCar.SessionId, CompoundName = otherClientCar.Status.CurrentTyreCompound });
        }

        // Hide all AI cars if requested
        if (_configuration.Extra.AiParams.HideAiCars)
        {
            foreach (EntryCarAi aiCar in _entryCarManager.AiCars)
            {
            
                SendPacket(new CSPCarVisibilityUpdate
                {
                    SessionId = aiCar.SessionId,
                    Visible = CSPCarVisibility.Invisible
                });
            }
        }

        // TODO: sent DRS zones

        SendPacket(CreateLapCompletedPacket(0xFF, 0, 0));

        if (_configuration.Extra.EnableClientMessages)
        {
            SendPacket(new CSPHandshakeIn
            {
                MinVersion = _configuration.CSPTrackOptions.MinimumCSPVersion ?? 0,
                RequiresWeatherFx = _configuration.Extra.EnableWeatherFx
            });
        }

        // Now the client is ready to receive game packets. Notify the API
        HasSentFirstUpdate = true;
        GameLoaded?.Invoke(this, EventArgs.Empty);
    }
        
    private LapCompletedOutgoing CreateLapCompletedPacket(byte sessionId, uint lapTime, int cuts)
    {
        // TODO: double check and rewrite this
        if (_sessionManager.CurrentSession.Results == null)
            throw new ArgumentNullException(nameof(_sessionManager.CurrentSession.Results));

        var laps = _sessionManager.CurrentSession.Results
            .Select(result => new LapCompletedOutgoing.CompletedLap
            {
                SessionId = result.Key,
                LapTime = _sessionManager.CurrentSession.Configuration.Type == SessionType.Race ? result.Value.TotalTime : result.Value.BestLap,
                NumLaps = (ushort)result.Value.NumLaps,
                HasCompletedLastLap = (byte)(result.Value.HasCompletedLastLap ? 1 : 0)
            })
            .OrderBy(lap => lap.LapTime); // TODO wrong for race sessions?

        return new LapCompletedOutgoing
        {
            SessionId = sessionId,
            LapTime = lapTime,
            Cuts = (byte)cuts,
            Laps = laps.ToArray(),
            TrackGrip = _weatherManager.CurrentWeather.TrackGrip
        };
    }

    internal bool TryAssociateUdp(SocketAddress endpoint)
    {
        if (UdpEndpoint != null)
            return false;

        UdpEndpoint = endpoint;
        return true;
    }

    public async Task BeginDisconnectAsync()
    {
        try
        {
            if (Interlocked.CompareExchange(ref _disconnectRequested, 1, 0) == 1)
                return;
            
            await Task.Yield();
            
            if (!string.IsNullOrEmpty(Name))
            {
                Logger.Debug("Disconnecting {ClientName} ({$ClientIpEndpoint})", Name, TcpClient.Client.RemoteEndPoint);
                Disconnected?.Invoke(this, EventArgs.Empty);
            }

            OutgoingPacketChannel.Writer.TryComplete();
            _ = await Task.WhenAny(Task.Delay(2000), SendLoopTask);

            try
            {
                await DisconnectTokenSource.CancelAsync();
                DisconnectTokenSource.Dispose();
            }
            catch (ObjectDisposedException) { }

            if (IsConnected)
            {
                await _entryCarManager.HandleClientDisconnected(this);
                if (HasPassedChecksum)
                    _acServer.BroadcastPacket(new CarDisconnected { SessionId = SessionId });

                // The client has disconnected.
                ClientCar = null;
                IsConnected = false;
                _acServer.NotifyClientDisconnected(this);
            }

            TcpClient.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error disconnecting {ClientName}", Name);
        }
    }
    
    private static string IdFromGuid(ulong guid)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes($"antarcticfurseal{guid}"));
        StringBuilder sb = new StringBuilder();
        foreach (byte b in hash)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }
}

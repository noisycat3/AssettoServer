using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Services;
using AssettoServer.Shared.Utils;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AssettoServer.Network.Udp;

internal class ACUdpServer : CriticalBackgroundService
{
    private readonly ACServerConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly CSPClientMessageHandler _clientMessageHandler;
    private readonly ushort _port;
    private readonly Socket _socket;
    
    private readonly ConcurrentDictionary<SocketAddress, EntryCarClient> _endpointCars = new();
    private static readonly byte[] CarConnectResponse = { (byte)ACServerProtocol.CarConnect };
    private readonly byte[] _lobbyCheckResponse;

    public ACUdpServer(SessionManager sessionManager,
        ACServerConfiguration configuration,
        EntryCarManager entryCarManager,
        IHostApplicationLifetime applicationLifetime,
        CSPClientMessageHandler clientMessageHandler) : base(applicationLifetime)
    {
        _sessionManager = sessionManager;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _clientMessageHandler = clientMessageHandler;
        _port = _configuration.Server.UdpPort;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        
        _lobbyCheckResponse = new byte[3];
        _lobbyCheckResponse[0] = (byte)ACServerProtocol.LobbyCheck;
        ushort httpPort = _configuration.Server.HttpPort;
        MemoryMarshal.Write(_lobbyCheckResponse.AsSpan()[1..], in httpPort);
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("Starting UDP server on port {Port}", _port);
        
        _socket.Bind(new IPEndPoint(IPAddress.Any, _port));
        await Task.Factory.StartNew(() => ReceiveLoop(stoppingToken), TaskCreationOptions.LongRunning);
    }

    private void ReceiveLoop(CancellationToken stoppingToken)
    {
        byte[] buffer = new byte[1500];
        var address = new SocketAddress(AddressFamily.InterNetwork);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var bytesRead = _socket.ReceiveFrom(buffer, SocketFlags.None, address);
                OnReceived(address, buffer, bytesRead);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in UDP receive loop");
            }
        }
    }

    public void Send(SocketAddress address, byte[] buffer, int offset, int size)
    {
        _socket.SendTo(buffer.AsSpan().Slice(offset, size), SocketFlags.None, address);
    }

    private void OnReceived(SocketAddress address, byte[] buffer, int size)
    {
        // moved to separate method because it always allocated a closure
        void HighPingKickAsync(EntryCarClient car)
        {
            _ = Task.Run(() => car.Server.KickAsync(car.Client as ACTcpClient, $"high ping ({car.Ping}ms)"));
        }
        
        try
        {
            var packetReader = new PacketReader(null, buffer.AsMemory()[..size]);

            var packetId = (ACServerProtocol)packetReader.Read<byte>();

            //Log.Logger.Information("Received packet {packetId}, len: {size}", packetId, size);

            if (packetId == ACServerProtocol.CarConnect)
            {
                int sessionId = packetReader.Read<byte>();
                if (_entryCarManager.EntryCars[sessionId] is EntryCarClient { Client: ACTcpClient client } clientCar)
                {
                    var clonedAddress = address.Clone();
                    if (client.TryAssociateUdp(clonedAddress))
                    {
                        _endpointCars[clonedAddress] = clientCar;
                        client.Disconnected += OnClientDisconnecting;

                        Send(clonedAddress, CarConnectResponse, 0, CarConnectResponse.Length);
                        //Log.Logger.Information("Sent CarConnectResponse packet {CarConnectResponse}, len: {Length}",
                        //    CarConnectResponse, CarConnectResponse.Length);
                    }
                }
            }
            else if (packetId == ACServerProtocol.LobbyCheck)
            {
                Send(address, _lobbyCheckResponse, 0, _lobbyCheckResponse.Length);
                //Log.Logger.Information("Sent lobby check UDP packet {_lobbyCheckResponse}, len: {_lobbyCheckResponse.Length}", 
                //    _lobbyCheckResponse, _lobbyCheckResponse.Length);
            }
            /*else if (packetId == 0xFF)
            {
                if (buffer.Length > 4
                    && packetReader.Read<byte>() == 0xFF
                    && packetReader.Read<byte>() == 0xFF
                    && packetReader.Read<byte>() == 0xFF)
                {
                    Log.Debug("Steam packet received");

                    byte[] data = buffer.AsSpan().ToArray();
                    Server.Steam.HandleIncomingPacket(data, remoteEp);
                }
            }*/
            else if (_endpointCars.TryGetValue(address, out EntryCarClient? car))
            {
                ACTcpClient? client = car.Client as ACTcpClient;
                if (client == null) 
                    return;
                
                if (packetId == ACServerProtocol.SessionRequest)
                {
                    if (_sessionManager.CurrentSession.Configuration.Type != packetReader.Read<SessionType>())
                        _sessionManager.SendCurrentSession(client);
                }
                else if (packetId == ACServerProtocol.PositionUpdate)
                {
                    if (!client.HasReceivedFirstPositionUpdate)
                        client.ReceivedFirstPositionUpdate();

                    if (!client.HasPassedChecksum
                        || client.SecurityLevel < _configuration.Extra.MandatoryClientSecurityLevel) return;

                    if (!client.HasSentFirstUpdate)
                        client.SendFirstUpdate();

                    car.UpdateClientPosition(packetReader.Read<PositionUpdateIn>());
                }
                else if (packetId == ACServerProtocol.PingPong)
                {
                    long currentTime = _sessionManager.ServerTimeMilliseconds;
                    ushort ping = (ushort)(currentTime - packetReader.Read<int>());
                    int timeOffset = (int)currentTime - ((car.Ping / 2) + packetReader.Read<int>());

                    car.UpdateTimingValues(ping, timeOffset); 
                    car.LastPongTime = currentTime;

                    if (car.Ping > _configuration.Extra.MaxPing)
                    {
                        if (car.HighPingSecondsStart.HasValue)
                        {
                            long millisecondsSinceHighPing = currentTime - car.HighPingSecondsStart.Value;
                            if (millisecondsSinceHighPing > (_configuration.Extra.MaxPingSeconds * 1000))
                                HighPingKickAsync(car);
                        }
                        else
                        {
                            car.HighPingSecondsStart = currentTime;
                        }
                    }
                    else
                    {
                        car.HighPingSecondsStart = null;
                    }
                }
                else if (_configuration.Extra.EnableUdpClientMessages && packetId == ACServerProtocol.Extended)
                {
                    var extendedId = packetReader.Read<CSPMessageTypeUdp>();
                    if (extendedId == CSPMessageTypeUdp.ClientMessage)
                    {
                        _clientMessageHandler.OnCSPClientMessageUdp(client, packetReader);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while receiving a UDP packet");
        }
    }

    private void OnClientDisconnecting(IClient sender, EventArgs args)
    {
        //if (sender.UdpEndpoint != null)
        if (sender is ACTcpClient { UdpEndpoint: {} endpoint })
        {
            _endpointCars.TryRemove(endpoint, out _);
        }
    }
}

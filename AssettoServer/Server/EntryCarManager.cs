using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Admin;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.OpenSlotFilters;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Shared.Utils;
using Serilog;

namespace AssettoServer.Server;

public class EntryCarManager
{
    // List of car slots on the server. Multiple AI instances can share one EntryCarAi slot.
    public EntryCarBase[] EntryCars { get; private set; } = Array.Empty<EntryCarBase>();
    public int ConnectedClientCount => ClientCars.Count(c => c.Client != null);
    public bool HasConnectedClients => EntryCars.Any(c => (c is EntryCarClient { Client: { } }));

    // Helper to get all clients
    public IEnumerable<EntryCarClient> ClientCars => EntryCars.OfType<EntryCarClient>();

    // Helper to get all ai cars (each can have multiple instances)
    public IEnumerable<EntryCarAi> AiCars => EntryCars.OfType<EntryCarAi>();
    
    private readonly ACServerConfiguration _configuration;
    private readonly IAdminService _adminService;
    private readonly Lazy<SessionManager> _sessionManager;
    private readonly Lazy<OpenSlotFilterChain> _openSlotFilterChain;

    private readonly EntryCarClient.Factory _factoryEntryCarClient;
    private readonly EntryCarAi.Factory _factoryEntryCarAi;

    // Connection semaphore
    private readonly SemaphoreSlim _connectSemaphore = new(1, 1);

    // Internal data
    private struct CarStatusUpdate
    {
        public byte SessionId;
        public ushort Ping;
        public int TimeOffset;
        public CarStatus Status;
    }

    private readonly CountedArray<CarStatusUpdate>[] _statusUpdates;

    public EntryCarManager(ACServerConfiguration configuration, IAdminService adminService, Lazy<SessionManager> sessionManager,
        Lazy<OpenSlotFilterChain> openSlotFilterChain, EntryCarClient.Factory factoryEntryCarClient, EntryCarAi.Factory factoryEntryCarAi)
    {
        _configuration = configuration;
        _adminService = adminService;
        _sessionManager = sessionManager;
        _openSlotFilterChain = openSlotFilterChain;

        _factoryEntryCarClient = factoryEntryCarClient;
        _factoryEntryCarAi = factoryEntryCarAi;

        // N^2 array of position updates - around 3MB with 200 cars. We good.
        int maxCars = configuration.EntryList.Cars.Count;
        _statusUpdates = new CountedArray<CarStatusUpdate>[maxCars];
        for (int i = 0; i < maxCars; i++)
            _statusUpdates[i] = new CountedArray<CarStatusUpdate>(maxCars);
    }

    internal async Task HandleClientDisconnected(ACTcpClient client)
    {
        try
        {
            await _connectSemaphore.WaitAsync();
            if (client.IsConnected)
            {
                if (client.ClientCar != EntryCars[client.SessionId])
                    client.Logger.Warning("{ClientName} has bad car reference!", client.Name);

                if (EntryCars[client.SessionId].Client == client && client.ClientCar is { } c)
                {
                    if (c.ClearClient(client))
                        c.ResetCarState();
                }
                else
                {
                    client.Logger.Error("{ClientName} is not registered in the client database!", client.Name);
                }

                client.Logger.Information("{ClientName} has disconnected", client.Name);
            }
        }
        catch (Exception ex)
        {
            client.Logger.Error(ex, "Error disconnecting {ClientName}", client.Name);
        }
        finally
        {
            _connectSemaphore.Release();
        }
    }

    internal async Task<bool> TrySecureSlotAsync(ACTcpClient client, string requestedModel)
    {
        try
        {
            await _connectSemaphore.WaitAsync();

            EntryCarClient? bestCarForClient = null;
            foreach (EntryCarClient clientCar in ClientCars)
            {
                if (clientCar.Model != requestedModel || clientCar.Client != null)
                    continue;

                bool isAdmin = await _adminService.IsAdminAsync(client.Guid);
                if (!isAdmin && !_openSlotFilterChain.Value.IsSlotOpen(clientCar, client.Guid))
                    continue;

                bestCarForClient = clientCar;
                break;
            }

            // Fail if we didn't find a suitable car
            if (bestCarForClient == null)
                return false;
            
            bestCarForClient.AssignClient(client);
            client.ClientCar = bestCarForClient;
            client.SessionId = bestCarForClient.SessionId;

            return true;
        }
        catch (Exception ex)
        {
            client.Logger.Error(ex, "Error securing slot for {ClientName}", client.Name);
        }
        finally
        {
            _connectSemaphore.Release();
        }

        return false;
    }

    internal void Initialize()
    {
        // OLD AI: Setup EntryCar array
        EntryCars = new EntryCarBase[_configuration.EntryList.Cars.Count];
        Log.Information("Loaded {Count} cars", EntryCars.Length);

        bool isAiEnabled = _configuration.Extra.EnableAi;
        for (int i = 0; i < EntryCars.Length; i++)
        {
            var entry = _configuration.EntryList.Cars[i];
            var driverOptions = CSPDriverOptions.Parse(entry.Skin);

            bool isAiCarEntry = false;
            if (entry.AiEnable)
            {
                if (isAiEnabled)
                    isAiCarEntry = true;
                else
                    Log.Logger.Warning("Ai enabled on entry {i}, but disabled in config! AI won't be enabled!", i);
            }

            EntryCarBase car;
            if (isAiCarEntry)
            {
                EntryCarAi aiCar = _factoryEntryCarAi.Invoke((byte)i, entry);
                car = aiCar;
            }
            else
            {
                EntryCarClient clientCar = _factoryEntryCarClient.Invoke((byte)i, entry);
                if (!string.IsNullOrWhiteSpace(entry.Guid))
                {
                    clientCar.AllowedGuids = entry.Guid.Split(';').Select(ulong.Parse).ToList();
                }
                car = clientCar;
            }

            car.Ballast = entry.Ballast;
            car.Restrictor = entry.Restrictor;
            car.DriverOptionsFlags = driverOptions;
            EntryCars[i] = car;
        }
    }

    internal void Update()
    {
        // Update all cars
        foreach (EntryCarBase car in EntryCars)
            car.UpdateCar();

        // Clear old car data
        foreach (CountedArray<CarStatusUpdate> updateList in _statusUpdates)
            updateList.Clear();

        // Prepare car data to send
        foreach (EntryCarClient clientCar in ClientCars)
        {
            if (clientCar.Client == null) 
                continue;

            IClient client = clientCar.Client;
            if (!client.InGame || !client.IsUdpReady)
                continue;

            foreach (EntryCarBase otherCar in EntryCars)
            {
                if (!otherCar.HasUpdateToSend)
                    continue;

                CarStatus? status = otherCar.GetPositionUpdateForClient(clientCar);
                if (status == null)
                    continue;

                // Register the update
                _statusUpdates[client.SessionId].Add(new CarStatusUpdate()
                {
                    SessionId = otherCar.SessionId,
                    Ping = otherCar.Ping,
                    TimeOffset = clientCar.TimeOffset,
                    Status = status,
                });
            }
        }
        
        // Now send all the updates through the server in an async manner
        for (int updateListIndex = 0; updateListIndex < _statusUpdates.Length; updateListIndex++)
        {
            if (_statusUpdates[updateListIndex].Count == 0)
                continue;

            byte sessionId = (byte)updateListIndex;
            long timeMs = _sessionManager.Value.ServerTimeMilliseconds;

            // Client still valid
            EntryCarBase baseCar = EntryCars[sessionId];
            if ((baseCar.Client is not ACTcpClient { ClientCar: { } clientCar } client) || (clientCar != baseCar))
                return;

            CountedArray<CarStatusUpdate> updateList = _statusUpdates[sessionId];
            PositionUpdateOut[] packetArray = updateList
                .Select(u => Private_MakePositionUpdateFromCarState(u.SessionId, u.Ping, u.TimeOffset, u.Status))
                .ToArray();

            const int chunkSize = 20;
            if (client.SupportsCSPCustomUpdate)
            {
                for (int packetIndex = 0; packetIndex < updateList.Count; packetIndex += chunkSize)
                {
                    client.SendPacketUdp(new CSPPositionUpdate(
                        new ArraySegment<PositionUpdateOut>(packetArray, packetIndex, Math.Min(chunkSize, packetArray.Length - packetIndex))));
                }
            }
            else
            {
                for (int packetIndex = 0; packetIndex < updateList.Count; packetIndex += chunkSize)
                {
                    client.SendPacketUdp(packetArray[packetIndex]);
                    //client.SendPacketUdp(new BatchedPositionUpdate((uint)(timeMs - clientCar.TimeOffset), clientCar.Ping,
                    //    new ArraySegment<PositionUpdateOut>(packetArray, packetIndex, Math.Min(chunkSize, packetArray.Length - packetIndex))));
                }
            }

            // Allow cars to clean up after update
            foreach (EntryCarBase car in EntryCars)
                car.PostUpdateCar();
        }
    }

    private static PositionUpdateOut Private_MakePositionUpdateFromCarState(byte sessionId, ushort ping, int timeOffset, CarStatus status)
    {
        return new PositionUpdateOut(sessionId,
            status.PakSequenceId,
            (uint)(status.Timestamp - timeOffset),
            ping,
            status.Position,
            status.Rotation,
            status.Velocity,
            status.TyreAngularSpeed[0],
            status.TyreAngularSpeed[1],
            status.TyreAngularSpeed[2],
            status.TyreAngularSpeed[3],
            status.SteerAngle,
            status.WheelAngle,
            status.EngineRpm,
            status.Gear,
            status.StatusFlag,
            status.PerformanceDelta,
            status.Gas);
    }
}

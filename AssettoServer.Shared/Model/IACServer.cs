using AssettoServer.Shared.Network.Packets.Outgoing;

namespace AssettoServer.Shared.Model;

public interface IACServer
{
    //  ************************
    //  BEGIN IACServer EVENTS

    /// <summary>
    /// Fires when a client has secured a slot and established a TCP connection.
    /// The client handshake can still be denied with IClient.HandshakeAccepted
    /// </summary>
    public event EventHandler<IACServer, ClientConnectionEventArgs>? ClientConnected;

    /// <summary>
    /// Fires when a client has been kicked.
    /// </summary>
    public event EventHandler<IACServer, ClientAuditEventArgs>? ClientKicked;

    /// <summary>
    /// Fires when a client has been banned.
    /// </summary>
    public event EventHandler<IACServer, ClientAuditEventArgs>? ClientBanned;

    /// <summary>
    /// Fires when a player has disconnected.
    /// </summary>
    public event EventHandler<IACServer, ClientConnectionEventArgs>? ClientDisconnected;

    //  END IACServer EVENTS
    //  ************************


    public IEnumerable<IEntryCar> GetAllCars();
    public IEnumerable<IClient> GetAllClients();
    public int GetMaxSessionId();
    public IEntryCar GetCarBySessionId(byte sessionId);
    public int GetUpdateRate();

    public void BroadcastPacket<TPacket>(TPacket packet, IClient? sender = null) where TPacket : IOutgoingNetworkPacket;

    public void BroadcastPacketUdp<TPacket>(in TPacket packet, IClient? sender = null, float? range = null, bool skipSender = true) where TPacket : IOutgoingNetworkPacket;

    public Task KickAsync(IClient? client, string? reason = null, IClient? admin = null);
}


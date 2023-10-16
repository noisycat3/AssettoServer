using System.Net.Sockets;

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
    /// Fires when a player has disconnected.
    /// </summary>
    public event EventHandler<IACServer, ClientConnectionEventArgs>? ClientDisconnected;

    //  END IACServer EVENTS
    //  ************************
}


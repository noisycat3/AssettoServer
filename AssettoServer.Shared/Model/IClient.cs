using System.Net;
using AssettoServer.Shared.Network.Packets.Outgoing;
using Serilog;

namespace AssettoServer.Shared.Model;

public interface IClient
{
    public byte SessionId { get; }
    public bool InGame { get; }
    public bool IsUdpReady { get; }
    public bool IsDisconnectRequested { get; }

    public string? Name { get; }
    public string? Team { get; }
    public string? NationCode { get; }

    public bool IsAdministrator { get; set; } // TODO: User groups?
    public ulong Guid { get; }          // GUID of the player playing on the server
    public string HashedGuid { get; }   // Hash of the player GUID
    public ulong? OwnerGuid { get; }    // GUID of the owner of the game license

    // Utility helpers
    public ILogger Logger { get; }
    public EndPoint? RemoteAddress { get; }
    public IEntryCar? CurrentEntryCar { get; }
    public CarStatus? CurrentCarStatus { get; }
    public int Ping { get; }


    // Send packet over TCP
    public void SendPacket<TPacket>(TPacket packet) where TPacket : IOutgoingNetworkPacket;

    // Send packet over UDP
    public void SendPacketUdp<TPacket>(in TPacket packet) where TPacket : IOutgoingNetworkPacket;

    // Start disconnecting the client
    public Task BeginDisconnectAsync();

    //  ************************
    //  BEGIN IClient EVENTS

    /// <summary>
    ///  Fires when a slot has been secured for a player and the handshake response is about to be sent.
    /// </summary>
    public event EventHandler<IClient, HandshakeAcceptedEventArgs> HandshakeAccepted;

    /// <summary>
    /// Fires when a client completes the checksum checks. This does not mean that the player has finished loading, use ClientFirstUpdateSent for that.
    /// </summary>
    public event EventHandler<IClient, ClientChecksumResultEventArgs>? ChecksumCompleted;

    /// <summary>
    /// Fires when a client has sent the first position update and becomes visible to other players.
    /// </summary>
    public event EventHandler<IClient, EventArgs>? GameLoaded;

    /// <summary>
    /// Fires when a player has disconnected.
    /// </summary>
    public event EventHandler<IClient, EventArgs>? Disconnected;

    /// <summary>
    /// Fires when a client has sent a chat message. Happens after checking for commands, etc. Custom chat filters can be implemented here.
    ///     Set ChatEventArgs.Cancel = true to stop it from being broadcast to other players.
    /// </summary>
    public event EventHandler<IClient, ChatMessageEventArgs>? ChatMessageEvent;

    /// <summary>
    /// Fires when a client has sent a chat message. Happens before checking for commands, etc. Custom command logic can be implemented here.
    ///     Set ChatEventArgs.Cancel = true to prevent further message processing.
    /// </summary>
    public event EventHandler<IClient, ChatMessageEventArgs>? ChatRawEvent;

    /// <summary>
    /// Fires when a client has completed a lap
    /// </summary>
    public event EventHandler<IClient, LapCompletedEventArgs>? LapCompleted;

    //  END IClient EVENTS
    //  ************************
}

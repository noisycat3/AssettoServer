using System.ComponentModel;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Shared.Network.Packets.Outgoing.Handshake;
using AssettoServer.Shared.Network.Packets.Shared;

namespace AssettoServer.Shared.Model;

public delegate void EventHandler<TSender, TArgs>(TSender sender, TArgs args) where TArgs : EventArgs;
public delegate void EventHandlerIn<TSender, TArg>(TSender sender, in TArg args) where TArg : struct;

public class ClientConnectionEventArgs : EventArgs
{
    public required IClient Client { get; init; }
}

public class HandshakeAcceptedEventArgs : EventArgs
{
    public required HandshakeResponse HandshakeResponse { get; init; }
}

public class ClientAuditEventArgs : ClientConnectionEventArgs
{
    public KickReason Reason { get; init; }
    public string? ReasonStr { get; init; }
    public IClient? Admin { get; init; }
}

public class ClientChecksumResultEventArgs : EventArgs
{
    public bool ChecksumValid { get; set; } = false;
}

public class ChatMessageEventArgs : CancelEventArgs
{
    public ChatMessage ChatMessage { get; init; }
}

public class SessionChangedEventArgs : EventArgs
{
    public ISessionState? PreviousSession { get; }
    public ISessionState NextSession { get; }

    public SessionChangedEventArgs(ISessionState? previousSession, ISessionState nextSession)
    {
        PreviousSession = previousSession;
        NextSession = nextSession;
    }
}

public class LapCompletedEventArgs : EventArgs
{
    public LapCompletedOutgoing Packet { get; }
    
    public LapCompletedEventArgs(LapCompletedOutgoing packet)
    {
        Packet = packet;
    }
}

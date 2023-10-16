using Qmmands;
using System.Text;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Shared;
using Serilog;
using AssettoServer.Shared.Network.Packets.Outgoing;

namespace AssettoServer.Commands;

public sealed class ACCommandContext : CommandContext
{
    public IACServer Server { get; }
    public IClient? Client { get; }

    public IRconClient? RconClient { get; }
    public int RconRequestId { get; }
    public StringBuilder? RconResponseBuilder { get; }
    

    public ACCommandContext(IACServer server, IClient client, IServiceProvider? serviceProvider = null) 
        : base(serviceProvider)
    {
        Server = server;
        Client = client;
    }

    public ACCommandContext(IACServer server, IRconClient client, int rconRequestId, IServiceProvider? serviceProvider = null) 
        : base(serviceProvider)
    {
        Server = server;
        RconResponseBuilder = new StringBuilder();
        RconClient = client;
        RconRequestId = rconRequestId;
    }

    public void Reply(string message)
    {
        Client?.SendPacket(new ChatMessage { SessionId = 255, Message = message });
        RconResponseBuilder?.AppendLine(message);
    }

    public void Broadcast(string message)
    {
        Log.Information("Broadcast: {Message}", message);
        RconResponseBuilder?.AppendLine(message);
        Server.BroadcastPacket(new ChatMessage { SessionId = 255, Message = message });
    }

    public void SendRconResponse()
    {
        if (RconClient == null || RconResponseBuilder == null) 
            return;
            
        RconClient.SendPacket(new RconResponseValuePacket
        {
            RequestId = RconRequestId,
            Body = RconResponseBuilder.ToString()
        });
    }
}

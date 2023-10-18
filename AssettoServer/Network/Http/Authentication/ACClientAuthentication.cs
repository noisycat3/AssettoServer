using AssettoServer.Server;
using AssettoServer.Shared.Network.Packets.Shared;

namespace AssettoServer.Network.Http.Authentication;

internal class ACClientAuthentication
{
    public ACClientAuthentication(CSPClientMessageTypeManager cspClientMessageTypeManager)
    {
        cspClientMessageTypeManager.RegisterClientMessageType(ApiKeyPacket.Id, (client, _) =>
        {
            client.SendPacket(new ApiKeyPacket { Key = client.ApiKey });
        });
    }
}

using AssettoServer.Shared.Network.Packets.Outgoing;

namespace AssettoServer.Shared.Model
{
    public enum RconProtocolIn
    {
        ExecCommand = 2,
        Auth = 3
    }

    public enum RconProtocolOut
    {
        ResponseValue = 0,
        AuthResponse = 2
    }

    public interface IRconClient
    {
        public void SendPacket<TPacket>(TPacket packet) where TPacket : IOutgoingNetworkPacket;
    }
}

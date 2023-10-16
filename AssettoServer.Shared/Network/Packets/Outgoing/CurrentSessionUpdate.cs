using AssettoServer.Shared.Model;

namespace AssettoServer.Shared.Network.Packets.Outgoing;

public class CurrentSessionUpdate : IOutgoingNetworkPacket
{
    public ISessionConfig? CurrentSession;
    public float TrackGrip;
    public IEnumerable<IEntryCar>? Grid;
    public long StartTime;

    public void ToWriter(ref PacketWriter writer)
    {
        ArgumentNullException.ThrowIfNull(CurrentSession);
        ArgumentNullException.ThrowIfNull(Grid);
        
        writer.Write((byte)ACServerProtocol.CurrentSessionUpdate);
        writer.WriteUTF8String(CurrentSession.Name);
        writer.Write((byte)CurrentSession.Id);
        writer.Write((byte)CurrentSession.Type);
        writer.Write((ushort)CurrentSession.Time);
        writer.Write((ushort)CurrentSession.Laps);
        writer.Write(TrackGrip);

        foreach (IEntryCar car in Grid)
            writer.Write(car.SessionId);

        writer.Write(StartTime);
    }
}

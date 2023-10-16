namespace AssettoServer.Shared.Network.Packets.Outgoing;

public class CarListResponse : IOutgoingNetworkPacket
{
    public int PageIndex;
    public int EntryCarsCount;
    public IEnumerable<Entry>? Entries;

    public struct Entry
    {
        public byte SessionId;
        public string Model;
        public string Skin;
        
        public string ClientName;
        public string TeamName;
        public string NationCode;
        public bool IsSpectator;
        public float[] DamageZoneLevel;
    }

    public void ToWriter(ref PacketWriter writer)
    {
        if (Entries == null)
            throw new ArgumentNullException(nameof(Entries));
            
        writer.Write((byte)ACServerProtocol.CarList);
        writer.Write((byte)PageIndex);
        writer.Write((byte)EntryCarsCount);

        foreach(Entry entry in Entries)
        {
            writer.Write(entry.SessionId);
            writer.WriteUTF8String(entry.Model);
            writer.WriteUTF8String(entry.Skin);

            writer.WriteUTF8String(entry.ClientName);
            writer.WriteUTF8String(entry.TeamName);
            writer.WriteUTF8String(entry.NationCode);
            writer.Write(entry.IsSpectator);

            foreach (float damageZoneLevel in entry.DamageZoneLevel)
                writer.Write(damageZoneLevel);
        }
    }
}

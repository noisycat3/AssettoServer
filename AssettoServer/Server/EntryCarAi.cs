using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;

namespace AssettoServer.Server;

public class EntryCarAi : EntryCarBase
{
    // IEntryCar implementation
    public override IClient? Client => null;
    public override bool IsAiCar => true;
    public override ushort Ping => 0;
    public override int TimeOffset => 0;
    public override string Name => "NPC";

    // Factory
    public delegate EntryCarAi Factory(byte inSessionId);

    public EntryCarAi(byte inSessionId, ACServer acServer, ACServerConfiguration configuration, EntryCarManager entryCarManager, SessionManager sessionManager)
        : base(inSessionId, acServer, configuration, entryCarManager, sessionManager)
    {

    }
}

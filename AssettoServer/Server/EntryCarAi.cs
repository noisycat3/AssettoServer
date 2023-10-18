using System;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;
using System.Collections.Generic;

namespace AssettoServer.Server;

internal class EntryCarAi : EntryCarBase
{
    // IEntryCar implementation
    public override IClient? Client => null;
    public override bool IsAiCar => true;
    public override ushort Ping => 0;
    public override int TimeOffset => 0;
    public override string Name => "NPC";

    public override bool HasUpdateToSend => true;

    public override int InstanceCount => _carInstances.Count;
    public override int InstanceMax => 20; // TODO: Max AI instances per car from config
    public override IEnumerable<ICarInstance> Instances => _carInstances;

    // List of all instances of this AI
    private readonly List<CarInstance> _carInstances;

    public override ICarInstance CreateInstance()
    {
        if (InstanceCount >= InstanceMax)
            throw new InvalidOperationException($"Too many instances of `{ Model }` ({SessionId})");

        CarInstance instance = new CarInstance(this);
        _carInstances.Add(instance);
        
        return instance;
    }

    public override void DestroyInstance(ICarInstance instance)
    {
        instance.DestroyInstance();

        int removed = _carInstances.RemoveAll(i => (i == instance));
        if (removed != 1)
            throw new InvalidOperationException($"While removing instance of `{Model}` encountered { removed } matching instances.");
    }

    // Factory
    public delegate EntryCarAi Factory(byte inSessionId, EntryList.Entry entry);

    public EntryCarAi(byte inSessionId, EntryList.Entry entry, 
        ACServer acServer, ACServerConfiguration configuration, SessionManager sessionManager)
        : base(inSessionId, entry, acServer, configuration, sessionManager)
    {
        _carInstances = new List<CarInstance>();
    }
}

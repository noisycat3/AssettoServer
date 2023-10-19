using System;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;
using System.Collections.Generic;
using System.Numerics;
using AssettoServer.Server.Ai.Configuration;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Utils;
using Microsoft.Extensions.Configuration;

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

    public override ICarInstance? GetBestInstanceFor(EntryCarClient clientCar)
    {
        if (clientCar.ClientCarInstance is not { } clientInstance)
            return null;

        float bestDistSq = float.MaxValue;
        ICarInstance? bestInst = null;

        foreach (ICarInstance inst in _carInstances)
        {
            float d = Vector3.DistanceSquared(clientInstance.Status.Position, inst.Status.Position);
            if (d >= bestDistSq)
                continue;

            bestInst = inst;
            bestDistSq = d;
        }

        return bestInst;
    }

    public override void UpdateCar(long currentTime)
    {
        foreach (CarInstance inst in _carInstances)
        {
            CarStatus status = inst.Status;
            status.PakSequenceId++;

            status.Timestamp = currentTime;
            // status.Position = Vector3.Zero;
            // status.Rotation = Vector3.Zero;
            // status.Velocity = Vector3.Zero;
            status.SteerAngle = 127;
            status.WheelAngle = 127;

            float tyreAngularSpeed = 0.0f;
            byte encodedTyreAngularSpeed = (byte)(Math.Clamp(MathF.Round(MathF.Log10(tyreAngularSpeed + 1.0f) * 20.0f) * Math.Sign(tyreAngularSpeed), -100.0f, 154.0f) + 100.0f);
            status.TyreAngularSpeed[0] = encodedTyreAngularSpeed;
            status.TyreAngularSpeed[1] = encodedTyreAngularSpeed;
            status.TyreAngularSpeed[2] = encodedTyreAngularSpeed;
            status.TyreAngularSpeed[3] = encodedTyreAngularSpeed;
            status.EngineRpm = (ushort)800;//MathUtils.Lerp(800, 3000, CurrentSpeed / _configuration.Extra.AiParams.MaxSpeedMs);
            status.StatusFlag = CarStatusFlags.LightsOn;
            status.Gear = 2;
        }
    }

    public override ICarInstance CreateInstance()
    {
        if (InstanceCount >= InstanceMax)
            throw new InvalidOperationException($"Too many instances of `{ Model }` ({SessionId})");

        CarInstance instance = new CarInstance(this);
        _carInstances.Add(instance);
        ACServer.NotifyCarInstanceSpawned(instance);

        return instance;
    }

    public override void DestroyInstance(ICarInstance instance)
    {
        int removed = _carInstances.RemoveAll(i => (i == instance));
        if (removed != 1)
            throw new InvalidOperationException($"While removing instance of `{Model}` encountered { removed } matching instances.");

        instance.HandleDestruction();
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

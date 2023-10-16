﻿using AssettoServer.Shared.Model;

namespace AssettoServer.Shared.Network.Packets.Outgoing.Handshake;

public class HandshakeResponse : IOutgoingNetworkPacket
{
    public string ServerName = "";
    public ushort UdpPort;
    public byte RefreshRateHz;
    public string TrackName = "";
    public string TrackConfig = "";
    public string CarModel = "";
    public string CarSkin = "";
    public float SunAngle;
    public short AllowedTyresOutCount;
    public bool AllowTyreBlankets;
    public byte TractionControlAllowed;
    public byte ABSAllowed;
    public bool StabilityAllowed;
    public bool AutoClutchAllowed;
    public byte JumpStartPenaltyMode;
    public float MechanicalDamageRate;
    public float FuelConsumptionRate;
    public float TyreConsumptionRate;
    public bool IsVirtualMirrorForced;
    public byte MaxContactsPerKm;
    public int RaceOverTime;
    public int ResultScreenTime;
    public bool HasExtraLap;
    public bool IsGasPenaltyDisabled;
    public short PitWindowStart;
    public short PitWindowEnd;
    public short InvertedGridPositions;
    public byte SessionId;
    public byte SessionCount;
    public IEnumerable<ISessionConfig> Sessions = null!;
    public ISessionConfig CurrentSession = null!;
    public long SessionTime;
    public float TrackGrip;
    public byte SpawnPosition;
    public byte ChecksumCount;
    public IEnumerable<string>? ChecksumPaths;
    public string LegalTyres = "";
    public int RandomSeed;
    public int CurrentTime;

    public void ToWriter(ref PacketWriter writer)
    {
        writer.Write((byte)ACServerProtocol.NewCarConnection);
        writer.WriteUTF32String(ServerName);
        writer.Write<ushort>(UdpPort);
        writer.Write(RefreshRateHz);
        writer.WriteUTF8String(TrackName);
        writer.WriteUTF8String(TrackConfig);
        writer.WriteUTF8String(CarModel);
        writer.WriteUTF8String(CarSkin);
        writer.Write(SunAngle);
        writer.Write(AllowedTyresOutCount);
        writer.Write(AllowTyreBlankets);
        writer.Write(TractionControlAllowed);
        writer.Write(ABSAllowed);
        writer.Write(StabilityAllowed);
        writer.Write(AutoClutchAllowed);
        writer.Write(JumpStartPenaltyMode);
        writer.Write(MechanicalDamageRate);
        writer.Write(FuelConsumptionRate);
        writer.Write(TyreConsumptionRate);
        writer.Write(IsVirtualMirrorForced);
        writer.Write(MaxContactsPerKm);
        writer.Write(RaceOverTime);
        writer.Write(ResultScreenTime);
        writer.Write(HasExtraLap);
        writer.Write(IsGasPenaltyDisabled);
        writer.Write(PitWindowStart);
        writer.Write(PitWindowEnd);
        writer.Write(InvertedGridPositions);
        writer.Write(SessionId);
        writer.Write(SessionCount);
        
        foreach(var sessionConfiguration in Sessions)
        {
            writer.Write((byte)sessionConfiguration.Type);
            writer.Write((ushort)sessionConfiguration.Laps);
            writer.Write((ushort)sessionConfiguration.Time);
        }

        writer.WriteUTF8String(CurrentSession.Name);
        writer.Write((byte)CurrentSession.Id);
        writer.Write((byte)CurrentSession.Type);
        writer.Write((ushort)CurrentSession.Time);
        writer.Write((ushort)CurrentSession.Laps);

        writer.Write(TrackGrip);
        writer.Write(SessionId);
        writer.Write(SessionTime);

        writer.Write(ChecksumCount);
        if (ChecksumPaths != null)
            foreach (string path in ChecksumPaths)
                writer.WriteUTF8String(path);

        writer.WriteUTF8String(LegalTyres);
        writer.Write(RandomSeed);
        writer.Write(CurrentTime);
    }
}

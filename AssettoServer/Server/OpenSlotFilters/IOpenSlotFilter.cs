﻿using System.Threading.Tasks;
using AssettoServer.Network.Tcp;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Outgoing.Handshake;

namespace AssettoServer.Server.OpenSlotFilters;

public interface IOpenSlotFilter
{
    void SetNextFilter(IOpenSlotFilter next);
    bool IsSlotOpen(IEntryCar entryCar, ulong guid);
    Task<AuthFailedResponse?> ShouldAcceptConnectionAsync(IClient client, HandshakeRequest request);
}

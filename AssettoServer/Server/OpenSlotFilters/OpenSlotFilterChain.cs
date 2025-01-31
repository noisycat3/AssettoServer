﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AssettoServer.Network.Tcp;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Outgoing.Handshake;

namespace AssettoServer.Server.OpenSlotFilters;

public class OpenSlotFilterChain
{
    private readonly IOpenSlotFilter _first;

    public OpenSlotFilterChain(IEnumerable<IOpenSlotFilter> filters)
    {
        IOpenSlotFilter? current = null;
        foreach (var filter in filters)
        {
            _first ??= filter;
            
            current?.SetNextFilter(filter);
            current = filter;
        }

        if (_first == null) throw new InvalidOperationException("No open slot filters set");
    }
    
    public bool IsSlotOpen(IEntryCar entryCar, ulong guid)
    {
        return _first.IsSlotOpen(entryCar, guid);
    }

    public Task<AuthFailedResponse?> ShouldAcceptConnectionAsync(IClient client, HandshakeRequest request)
    {
        return _first.ShouldAcceptConnectionAsync(client, request);
    }
}

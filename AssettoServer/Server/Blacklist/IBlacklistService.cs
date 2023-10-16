﻿using System;
using System.Threading.Tasks;
using AssettoServer.Shared.Model;

namespace AssettoServer.Server.Blacklist;

public interface IBlacklistService
{
    public Task<bool> IsBlacklistedAsync(ulong guid);
    public Task AddAsync(ulong guid, string reason = "", ulong? admin = null);

    public event EventHandler<IBlacklistService, EventArgs> Changed;
}

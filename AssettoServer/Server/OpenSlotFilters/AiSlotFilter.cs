﻿using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Model;

namespace AssettoServer.Server.OpenSlotFilters;

public class AiSlotFilter : OpenSlotFilterBase
{
    //private readonly EntryCarManager _entryCarManager;
    private readonly ACServerConfiguration _configuration;

    public AiSlotFilter(/*EntryCarManager entryCarManager, */ACServerConfiguration configuration)
    {
        //_entryCarManager = entryCarManager;
        _configuration = configuration;
    }

    public override bool IsSlotOpen(IEntryCar entryCar, ulong guid)
    {
        // OLD AI: AI Slot filter
        //if (entryCar.AiMode == AiMode.Fixed
        //    || (_configuration.Extra.AiParams.MaxPlayerCount > 0 && _entryCarManager.ConnectedCars.Count >= _configuration.Extra.AiParams.MaxPlayerCount))
        //{
        //    return false;
        //}

        return base.IsSlotOpen(entryCar, guid);
    }
}

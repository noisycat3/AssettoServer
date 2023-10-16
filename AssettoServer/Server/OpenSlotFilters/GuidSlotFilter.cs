using AssettoServer.Shared.Model;

namespace AssettoServer.Server.OpenSlotFilters;

public class GuidSlotFilter : OpenSlotFilterBase
{
    public override bool IsSlotOpen(IEntryCar entryCar, ulong guid)
    {
        if (entryCar is not EntryCarClient { } clientCar)
            return false;

        if (clientCar.AllowedGuids.Count > 0 && !clientCar.AllowedGuids.Contains(guid))
            return false;

        return base.IsSlotOpen(entryCar, guid);
    }
}

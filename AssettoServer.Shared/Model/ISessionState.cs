namespace AssettoServer.Shared.Model;

public interface ISessionState
{
    public ISessionConfig Configuration { get; }

    public int EndTime { get; }
    public long StartTimeMilliseconds { get; }
    public int TimeLeftMilliseconds { get; }
    public long SessionTimeMilliseconds { get; }
    public uint TargetLap { get; }
    public uint LeaderLapCount { get; }
    public bool LeaderHasCompletedLastLap { get; }
    public bool SessionOverFlag { get; }
    public Dictionary<byte, EntryCarResult>? Results { get; }
    public IEnumerable<IEntryCar>? Grid { get; }
}


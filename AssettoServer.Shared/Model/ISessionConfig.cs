namespace AssettoServer.Shared.Model;


public interface ISessionConfig
{
    public int Id { get; }
    public SessionType Type { get; }

    public string? Name { get; }
    public int Time { get; }
    public int Laps { get; }

    public uint WaitTime { get; }
    public bool IsOpen { get; }
    public bool Infinite { get; }

    public bool IsTimedRace { get; }
}

namespace AssettoServer.Shared.Model;

public interface IEntryCar
{
    IACServer Server { get; }
    public byte SessionId { get; }
    public string Model { get; }
    public string Skin { get; }
    public bool IsAiCar { get; }
    public IClient? Client { get; }
    public string Name { get; }
}


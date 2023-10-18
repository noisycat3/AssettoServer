namespace AssettoServer.Shared.Model;

public interface IEntryCar : ISessionObject
{
    IACServer Server { get; }
    public string Model { get; }
    public string Skin { get; }
    public bool IsAiCar { get; }
    public IClient? Client { get; }
    public string Name { get; }

    public int InstanceCount { get; }
    public int InstanceMax { get; }
    public IEnumerable<ICarInstance> Instances { get; }

    public ICarInstance CreateInstance();
    public void DestroyInstance(ICarInstance instance);
}


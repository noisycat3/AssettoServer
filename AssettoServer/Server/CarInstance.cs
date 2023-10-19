using AssettoServer.Shared.Model;
using System;

namespace AssettoServer.Server;

internal class CarInstance : ICarInstance
{
    public CarInstance(IEntryCar entry)
    {
        CarEntry = entry;
        Status = new CarStatus();
    }

    public IEntryCar CarEntry { get; }
    public CarStatus Status { get; private set; }
    
    public event EventHandler<ICarInstance, EventArgs>? Destroyed;

    public void DestroyInstance()
    {
        // Trigger the destruction process
        CarEntry.DestroyInstance(this);
    }

    public void HandleDestruction()
    {
        // Callback
        Destroyed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        Status = new CarStatus();
    }
}

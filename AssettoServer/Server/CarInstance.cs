using AssettoServer.Shared.Model;
using System;

namespace AssettoServer.Server;

internal class CarInstance : ICarInstance
{
    public CarInstance(IEntryCar entry)
    {
        CarEntry = entry;
        Status = new CarStatus();

        _isBeginDestroyed = false;
    }

    public IEntryCar CarEntry { get; }
    public CarStatus Status { get; private set; }

    private bool _isBeginDestroyed;
    public event EventHandler<ICarInstance, EventArgs>? Destroyed;

    public void DestroyInstance()
    {
        if (_isBeginDestroyed)
            return;
        _isBeginDestroyed = true;
        
        // This might call destroy instance again
        CarEntry.DestroyInstance(this);

        // Callback
        Destroyed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        Status = new CarStatus();
    }
}

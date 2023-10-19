using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssettoServer.Shared.Model;

public interface ICarInstance
{
    public IEntryCar CarEntry { get; }
    public CarStatus Status { get; }

    public void DestroyInstance();
    public void HandleDestruction();

    /// <summary>
    ///  Fires when a car instance is destroyed
    /// </summary>
    public event EventHandler<ICarInstance, EventArgs>? Destroyed;
}


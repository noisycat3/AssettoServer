using Serilog;

namespace AssettoServer.Shared.Plugin;

public enum EAssettoServerPluginUnloadReason
{
    Shutdown,
    Reload
}

public abstract class AssettoServerPlugin
{
    /// <summary>
    /// Called when the plugin is initialized by the server
    /// </summary>
    public abstract void OnLoad();

    /// <summary>
    /// Called when the plugin is unloaded
    /// </summary>
    /// <param name="reason"></param>
    public virtual void OnUnload(EAssettoServerPluginUnloadReason reason) { }

    /// <summary>
    /// Called when the server begins an update. This happens before sending position updates to the client.
    /// </summary>
    public virtual void OnUpdate(long serverTimeMs, long timeElapsedMs) { }

    public ILogger Logger => Log.Logger;
}


using AssettoServer.Shared.Model;

namespace AssettoServer.Shared.Utils;

/// <summary>
/// Creates a lookup table capable of indexing by ISessionObject.
/// Client and it's car are considered equal.
/// Each AI *slot* is a different key. To map AI data use AI module functions.
/// </summary>
public class LookupArray<T>
{
    public LookupArray(IACServer server)
    {
        _array = new T[server.GetMaxSessionId()];
    }

    public T this[byte sessionId]
    {
        get => _array[sessionId];
        set => _array[sessionId] = value;
    }

    public T this[ISessionObject obj]
    {
        get => this[obj.SessionId];
        set => this[obj.SessionId] = value;
    }

    private readonly T[] _array;
}


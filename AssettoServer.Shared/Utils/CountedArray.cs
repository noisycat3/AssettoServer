using System.Collections;

namespace AssettoServer.Shared.Utils;

public class CountedArray<T> : IEnumerable<T>
{
    public readonly T[] Array;
    public int Count { get; private set; } = 0;

    public CountedArray(int maxLength)
    {
        Array = new T[maxLength];
    }

    public void Add(T elem)
    {
        Array[Count++] = elem;
    }

    public void Clear()
    {
        Count = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
            yield return Array[i];
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

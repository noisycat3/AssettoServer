using System;
using System.Collections.Generic;
using System.Numerics;

namespace AssettoServer.Server;

public readonly struct SpatialHashMapCellIndex
{
    public readonly int X, Y;

    public SpatialHashMapCellIndex(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override bool Equals(object? obj)
    {
        if (obj is SpatialHashMapCellIndex o)
            return X == o.X && Y == o.Y;
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X.GetHashCode(), Y.GetHashCode());
    }
}

public interface ISpatialHashMapItem
{
    // Will be assigned by owning spatial hash map when needed
    SpatialHashMapCellIndex? CurrentSpatialCell { get; set; }
    
    Vector3 GetPositionForSpatialHashMap();
}

public class SpatialHashMap<T> where T : ISpatialHashMapItem
{
    public SpatialHashMap(Vector3 cellSize)
    {
        CellSize = cellSize;
    }

    public Vector3 CellSize { get; }

    public void RemoveItem(T item)
    {
        if (!item.CurrentSpatialCell.HasValue) 
            return;

        if (!_hashMap.TryGetValue(item.CurrentSpatialCell.Value, out List<T>? bucket))
            return;

        bucket.Remove(item);
        if (bucket.Count == 0)
            _hashMap.Remove(item.CurrentSpatialCell.Value);
    }

    private SpatialHashMapCellIndex PositionToCell(Vector3 pos)
    {
        return new(
            (int)Math.Floor(pos.X / CellSize.X),
            (int)Math.Floor(pos.Y / CellSize.Y)
        );
    }

    public void UpdateItem(T item)
    {
        RemoveItem(item);

        Vector3 pos = item.GetPositionForSpatialHashMap();
        SpatialHashMapCellIndex cell = PositionToCell(pos);

        item.CurrentSpatialCell = cell;
        if (_hashMap.TryGetValue(cell, out List<T>? bucket))
            bucket.Add(item);
        else
            _hashMap.Add(cell, new List<T>(new[] { item }));
    }

    public IEnumerable<T> FindNearby(Vector3 location, float radius)
    {
        Vector3 boxHalfSize = new Vector3(radius, 0, radius);

        SpatialHashMapCellIndex cellMin = PositionToCell(location - boxHalfSize);
        SpatialHashMapCellIndex cellMax = PositionToCell(location + boxHalfSize);

        for (int x = cellMin.X; x <= cellMax.X; x++)
        {
            for (int y = cellMin.Y; y <= cellMax.Y; y++)
            {
                if (!_hashMap.TryGetValue(new SpatialHashMapCellIndex(x, y), out List<T>? bucket)) 
                    continue;

                foreach (var item in bucket)
                    yield return item;
            }
        }
    }

    private readonly Dictionary<SpatialHashMapCellIndex, List<T>> _hashMap = new();
}

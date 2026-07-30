using Ash.Core;

namespace Ash.Sim;

[Flags]
public enum TerrainFlags : byte
{
    None = 0,
    Walkable = 1 << 0,
    Solid = 1 << 1,
    ProvidesSupport = 1 << 2,
    Hazard = 1 << 3,
}

public readonly record struct TerrainCell(
    int FloorZ,
    TerrainFlags Flags)
{
    public static TerrainCell Floor =>
        new(0, TerrainFlags.Walkable | TerrainFlags.ProvidesSupport);
}

public readonly record struct WorldRectangle(
    int XMin,
    int XMax,
    int YMin,
    int YMax)
{
    public int Width => XMax - XMin;

    public int Depth => YMax - YMin;

    public bool IsEmpty => Width <= 0 || Depth <= 0;

    public bool Overlaps(WorldRectangle other) =>
        XMin < other.XMax &&
        XMax > other.XMin &&
        YMin < other.YMax &&
        YMax > other.YMin;

    public bool Contains(int x, int y) =>
        x >= XMin && x < XMax && y >= YMin && y < YMax;

    public void Validate(string parameterName)
    {
        if (IsEmpty)
        {
            throw new ArgumentException(
                $"Rectangle must have positive extents: {this}.",
                parameterName);
        }
    }
}

public readonly record struct WorldVolume(
    WorldRectangle Horizontal,
    int ZMin,
    int ZMax)
{
    public bool IsEmpty => Horizontal.IsEmpty || ZMax <= ZMin;

    public bool Overlaps(WorldVolume other) =>
        Horizontal.Overlaps(other.Horizontal) &&
        ZMin < other.ZMax &&
        ZMax > other.ZMin;

    public void Validate(string parameterName)
    {
        if (IsEmpty)
        {
            throw new ArgumentException(
                $"Volume must have positive extents: {this}.",
                parameterName);
        }
    }
}

/// <summary>
/// The live maps of one world, keyed by map id. A map registers itself here
/// when it is constructed, which is what lets the object transaction validate
/// physical placement without a second world model.
/// </summary>
public sealed class WorldMapSet
{
    private readonly SortedDictionary<ushort, WorldMap> _maps = [];

    public WorldMap Get(ushort mapId) =>
        _maps.TryGetValue(mapId, out var map)
            ? map
            : throw new InvalidOperationException(
                $"Map {mapId} is not registered in this world.");

    public bool TryGet(ushort mapId, out WorldMap map) =>
        _maps.TryGetValue(mapId, out map!);

    internal void Register(WorldMap map)
    {
        if (_maps.TryGetValue(map.MapId, out var existing))
        {
            throw new InvalidOperationException(
                $"Map {map.MapId} is already registered as {existing}.");
        }

        _maps.Add(map.MapId, map);
    }

    internal void Unregister(WorldMap map)
    {
        if (_maps.TryGetValue(map.MapId, out var existing) &&
            ReferenceEquals(existing, map))
        {
            _maps.Remove(map.MapId);
        }
    }
}

/// <summary>
/// Authoritative terrain container and derived spatial index for one map.
/// Object identity and locations remain owned by <see cref="ObjectStore"/>.
/// </summary>
public sealed class WorldMap : IDisposable
{
    public const int DefaultBucketSize = 256;

    /// <summary>
    /// One terrain cell is 256 world units wide and deep. Object footprints are
    /// anchored at the far corner of the cell they occupy, so the anchor of the
    /// object in cell <c>c</c> is <c>(c + 1) * WorldUnitsPerTile</c>.
    /// </summary>
    public const int WorldUnitsPerTile = 256;

    private static readonly Dictionary<ObjectId, ObjectLocation>
        EmptyProjection = [];

    private readonly ObjectStore _objects;
    private readonly TerrainCell[] _terrain;
    private readonly Dictionary<(int X, int Y), ObjectId[]> _buckets = [];
    private readonly Dictionary<Vec3i, ObjectId[]> _anchors = [];
    private ObjectId[] _mapObjects = [];
    private bool _disposed;

    public WorldMap(
        ObjectStore objects,
        ushort mapId,
        int width,
        int depth,
        int bucketSize = DefaultBucketSize,
        TerrainCell? defaultTerrain = null)
    {
        _objects = objects ??
            throw new ArgumentNullException(nameof(objects));
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        if (bucketSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bucketSize));
        }

        MapId = mapId;
        Width = width;
        Depth = depth;
        BucketSize = bucketSize;
        WorldBounds = new WorldRectangle(
            0,
            checked(width * WorldUnitsPerTile),
            0,
            checked(depth * WorldUnitsPerTile));
        _terrain = Enumerable.Repeat(
                defaultTerrain ?? TerrainCell.Floor,
                checked(width * depth))
            .ToArray();
        _objects.Maps.Register(this);
        _objects.Committed += OnObjectStoreCommitted;
        RebuildIndex();
    }

    public ushort MapId { get; }

    public int Width { get; }

    public int Depth { get; }

    public int BucketSize { get; }

    /// <summary>The horizontal world extent every placed footprint must fit in.</summary>
    public WorldRectangle WorldBounds { get; }

    public long Revision { get; private set; }

    public int IndexedObjectCount => _mapObjects.Length;

    public TerrainCell GetTerrain(int x, int y)
    {
        ValidateTerrainCoordinate(x, y);
        return _terrain[(y * Width) + x];
    }

    public void SetTerrain(int x, int y, TerrainCell value)
    {
        ValidateTerrainCoordinate(x, y);
        _terrain[(y * Width) + x] = value;
        Revision++;
    }

    /// <summary>
    /// Every terrain cell of this map touched by <paramref name="bounds"/>,
    /// ordered by cell coordinate and clipped to the map.
    /// </summary>
    public IEnumerable<(int X, int Y, TerrainCell Cell)> TerrainSpan(
        WorldRectangle bounds)
    {
        bounds.Validate(nameof(bounds));
        var minX = Math.Max(0, FloorDiv(bounds.XMin, WorldUnitsPerTile));
        var maxX = Math.Min(
            Width - 1,
            FloorDiv(bounds.XMax - 1, WorldUnitsPerTile));
        var minY = Math.Max(0, FloorDiv(bounds.YMin, WorldUnitsPerTile));
        var maxY = Math.Min(
            Depth - 1,
            FloorDiv(bounds.YMax - 1, WorldUnitsPerTile));
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                yield return (x, y, _terrain[(y * Width) + x]);
            }
        }
    }

    public PlacementResult ValidatePlacement(
        WorldObject projected,
        ObjectLocation source) =>
        ValidatePlacement(projected, source, EmptyProjection);

    /// <summary>
    /// The one physical placement contract. <paramref name="projection"/> holds
    /// the destination location of every object moving in the same transaction,
    /// so overlap is tested against the projected world, not the committed one.
    /// </summary>
    public PlacementResult ValidatePlacement(
        WorldObject projected,
        ObjectLocation source,
        IReadOnlyDictionary<ObjectId, ObjectLocation> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projected.Location.Kind != LocationKind.OnMap ||
            projected.Location.MapId != MapId)
        {
            throw new InvalidOperationException(
                $"Object {projected.Id} is not placed on map {MapId}.");
        }

        var volume = VolumeFor(projected);
        var bounds = volume.Horizontal;
        if (bounds.XMin < WorldBounds.XMin ||
            bounds.XMax > WorldBounds.XMax ||
            bounds.YMin < WorldBounds.YMin ||
            bounds.YMax > WorldBounds.YMax)
        {
            return PlacementResult.Reject(
                PlacementFailure.OutOfMapBounds,
                PlacementBlocker.MapEdge,
                $"{projected.Name} does not fit inside map {MapId}.");
        }

        if (projected.HasFlag(ObjectFlags.Fixed) &&
            (source.Kind != LocationKind.OnMap ||
             source.MapId != MapId ||
             source.Position != projected.Location.Position))
        {
            return PlacementResult.Reject(
                PlacementFailure.Immovable,
                PlacementBlocker.Object(projected.Id),
                $"{projected.Name} is fixed in place.");
        }

        foreach (var (x, y, cell) in TerrainSpan(bounds))
        {
            if (cell.Flags.HasFlag(TerrainFlags.Solid) &&
                volume.ZMax > cell.FloorZ)
            {
                return PlacementResult.Reject(
                    PlacementFailure.TerrainBlocked,
                    PlacementBlocker.Terrain(x, y),
                    $"Solid terrain at ({x}, {y}) blocks " +
                    $"{projected.Name}.");
            }
        }

        if (!projected.HasFlag(ObjectFlags.Solid))
        {
            return PlacementResult.Allow($"{projected.Name} fits.");
        }

        foreach (var candidate in ProjectedSolids(bounds, projection))
        {
            if (candidate.Id == projected.Id ||
                !VolumeFor(candidate).Overlaps(volume))
            {
                continue;
            }

            return PlacementResult.Reject(
                PlacementFailure.ObjectBlocked,
                PlacementBlocker.Object(candidate.Id),
                $"{candidate.Name} occupies that space.");
        }

        return PlacementResult.Allow($"{projected.Name} fits.");
    }

    public IReadOnlyList<WorldObject> QueryAll(
        ObjectFlags requiredFlags = ObjectFlags.None) =>
        ResolveAndFilter(_mapObjects, requiredFlags);

    public IReadOnlyList<WorldObject> QueryAnchor(
        Vec3i anchor,
        ObjectFlags requiredFlags = ObjectFlags.None) =>
        _anchors.TryGetValue(anchor, out var ids)
            ? ResolveAndFilter(ids, requiredFlags)
            : [];

    public IReadOnlyList<WorldObject> Query(
        WorldRectangle region,
        ObjectFlags requiredFlags = ObjectFlags.None)
    {
        region.Validate(nameof(region));
        var ids = CandidateIds(region);
        return ResolveAndFilter(ids, requiredFlags)
            .Where(value => BoundsFor(value).Overlaps(region))
            .ToArray();
    }

    public IReadOnlyList<WorldObject> Query(
        WorldVolume volume,
        ObjectFlags requiredFlags = ObjectFlags.None)
    {
        volume.Validate(nameof(volume));
        var ids = CandidateIds(volume.Horizontal);
        return ResolveAndFilter(ids, requiredFlags)
            .Where(value => VolumeFor(value).Overlaps(volume))
            .ToArray();
    }

    public void ValidateIndex()
    {
        var expected = _objects.Enumerate()
            .Where(IsOnThisMap)
            .Select(value => value.Id)
            .Order()
            .ToArray();
        if (!expected.SequenceEqual(_mapObjects))
        {
            throw new InvalidOperationException(
                "WorldMap object list disagrees with ObjectStore.");
        }

        foreach (var value in QueryAll())
        {
            var bounds = BoundsFor(value);
            foreach (var cell in BucketSpan(bounds))
            {
                if (!_buckets.TryGetValue(cell, out var bucket) ||
                    Array.BinarySearch(bucket, value.Id) < 0)
                {
                    throw new InvalidOperationException(
                        $"Spatial bucket {cell} is missing {value.Id}.");
                }
            }

            if (!_anchors.TryGetValue(value.Location.Position, out var anchors) ||
                Array.BinarySearch(anchors, value.Id) < 0)
            {
                throw new InvalidOperationException(
                    $"Anchor index is missing {value.Id}.");
            }
        }

        foreach (var bucket in _buckets.Values)
        {
            if (!bucket.SequenceEqual(bucket.Distinct().Order()))
            {
                throw new InvalidOperationException(
                    "A spatial bucket is duplicated or not deterministic.");
            }

            foreach (var id in bucket)
            {
                var value = _objects.Get(id);
                if (!IsOnThisMap(value) ||
                    !BucketSpan(BoundsFor(value)).Any(cell =>
                        ReferenceEquals(bucket, _buckets[cell])))
                {
                    throw new InvalidOperationException(
                        $"Spatial bucket contains stale object {id}.");
                }
            }
        }

        foreach (var (anchor, ids) in _anchors)
        {
            if (!ids.SequenceEqual(ids.Distinct().Order()))
            {
                throw new InvalidOperationException(
                    "An anchor bucket is duplicated or not deterministic.");
            }

            foreach (var id in ids)
            {
                var value = _objects.Get(id);
                if (!IsOnThisMap(value) ||
                    value.Location.Position != anchor)
                {
                    throw new InvalidOperationException(
                        $"Anchor index contains stale object {id}.");
                }
            }
        }
    }

    public static WorldRectangle BoundsFor(WorldObject value)
    {
        if (value.Location.Kind != LocationKind.OnMap)
        {
            throw new InvalidOperationException(
                $"Object {value.Id} is not on a map.");
        }

        var position = value.Location.Position;
        return new WorldRectangle(
            checked(position.X - value.Footprint.Width),
            position.X,
            checked(position.Y - value.Footprint.Depth),
            position.Y);
    }

    public static WorldVolume VolumeFor(WorldObject value)
    {
        var position = value.Location.Position;
        return new WorldVolume(
            BoundsFor(value),
            position.Z,
            checked(position.Z + value.Height));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _objects.Committed -= OnObjectStoreCommitted;
        _objects.Maps.Unregister(this);
        _disposed = true;
    }

    /// <summary>
    /// Solid objects whose projected location overlaps <paramref name="bounds"/>,
    /// in <see cref="ObjectId"/> order. Objects moving in the same transaction
    /// are considered at their destination, wherever the index still holds them.
    /// </summary>
    private IEnumerable<WorldObject> ProjectedSolids(
        WorldRectangle bounds,
        IReadOnlyDictionary<ObjectId, ObjectLocation> projection)
    {
        var candidates = new SortedSet<ObjectId>(CandidateIds(bounds));
        candidates.UnionWith(projection.Keys);
        foreach (var id in candidates)
        {
            var value = _objects.Get(id);
            var location = projection.TryGetValue(id, out var projected)
                ? projected
                : value.Location;
            if (location.Kind != LocationKind.OnMap ||
                location.MapId != MapId)
            {
                continue;
            }

            var snapshot = value with { Location = location };
            if (snapshot.HasFlag(ObjectFlags.Solid))
            {
                yield return snapshot;
            }
        }
    }

    private void OnObjectStoreCommitted(ObjectStoreCommit _commit) =>
        RebuildIndex();

    private void RebuildIndex()
    {
        var values = _objects.Enumerate()
            .Where(IsOnThisMap)
            .OrderBy(value => value.Id)
            .ToArray();
        var buckets = new Dictionary<(int X, int Y), List<ObjectId>>();
        var anchors = new Dictionary<Vec3i, List<ObjectId>>();
        foreach (var value in values)
        {
            foreach (var cell in BucketSpan(BoundsFor(value)))
            {
                if (!buckets.TryGetValue(cell, out var bucket))
                {
                    bucket = [];
                    buckets.Add(cell, bucket);
                }

                bucket.Add(value.Id);
            }

            var anchor = value.Location.Position;
            if (!anchors.TryGetValue(anchor, out var anchored))
            {
                anchored = [];
                anchors.Add(anchor, anchored);
            }

            anchored.Add(value.Id);
        }

        _buckets.Clear();
        foreach (var (cell, ids) in buckets)
        {
            _buckets.Add(cell, ids.Distinct().Order().ToArray());
        }

        _anchors.Clear();
        foreach (var (anchor, ids) in anchors)
        {
            _anchors.Add(anchor, ids.Distinct().Order().ToArray());
        }

        _mapObjects = values.Select(value => value.Id).ToArray();
        Revision++;
    }

    private IReadOnlyList<ObjectId> CandidateIds(WorldRectangle region)
    {
        var candidates = new HashSet<ObjectId>();
        foreach (var cell in BucketSpan(region))
        {
            if (_buckets.TryGetValue(cell, out var bucket))
            {
                candidates.UnionWith(bucket);
            }
        }

        return candidates.Order().ToArray();
    }

    private IReadOnlyList<WorldObject> ResolveAndFilter(
        IEnumerable<ObjectId> ids,
        ObjectFlags requiredFlags)
    {
        var result = new List<WorldObject>();
        foreach (var id in ids)
        {
            if (!_objects.TryGet(id, out var value) ||
                !IsOnThisMap(value) ||
                (requiredFlags != ObjectFlags.None &&
                 (value.Flags & requiredFlags) != requiredFlags))
            {
                continue;
            }

            result.Add(value);
        }

        return result;
    }

    private IEnumerable<(int X, int Y)> BucketSpan(
        WorldRectangle bounds)
    {
        var minX = FloorDiv(bounds.XMin, BucketSize);
        var maxX = FloorDiv(bounds.XMax - 1, BucketSize);
        var minY = FloorDiv(bounds.YMin, BucketSize);
        var maxY = FloorDiv(bounds.YMax - 1, BucketSize);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                yield return (x, y);
            }
        }
    }

    private bool IsOnThisMap(WorldObject value) =>
        value.Location.Kind == LocationKind.OnMap &&
        value.Location.MapId == MapId;

    private void ValidateTerrainCoordinate(int x, int y)
    {
        if (x < 0 || x >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        if (y < 0 || y >= Depth)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor != 0 && value < 0
            ? quotient - 1
            : quotient;
    }
}

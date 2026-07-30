namespace Ash.Sim;

public enum SupportKind : byte
{
    None = 0,
    Terrain = 1,
    Object = 2,
}

/// <summary>
/// The one authoritative answer to "what is this object resting on?". Support
/// back-links are rebuilt from these references, never the other way round.
/// </summary>
public readonly record struct SupportRef
{
    private readonly ushort _mapId;
    private readonly int _cellX;
    private readonly int _cellY;
    private readonly ObjectId _objectId;
    private readonly int _topZ;

    private SupportRef(
        SupportKind kind,
        ushort mapId = 0,
        int cellX = 0,
        int cellY = 0,
        ObjectId objectId = default,
        int topZ = 0)
    {
        Kind = kind;
        _mapId = mapId;
        _cellX = cellX;
        _cellY = cellY;
        _objectId = objectId;
        _topZ = topZ;
    }

    public SupportKind Kind { get; }

    public bool IsNone => Kind == SupportKind.None;

    /// <summary>The elevation of the supporting surface's top face.</summary>
    public int TopZ => Kind != SupportKind.None
        ? _topZ
        : throw WrongKind(nameof(TopZ), SupportKind.Terrain, SupportKind.Object);

    public ushort MapId => Kind == SupportKind.Terrain
        ? _mapId
        : throw WrongKind(nameof(MapId), SupportKind.Terrain);

    public int CellX => Kind == SupportKind.Terrain
        ? _cellX
        : throw WrongKind(nameof(CellX), SupportKind.Terrain);

    public int CellY => Kind == SupportKind.Terrain
        ? _cellY
        : throw WrongKind(nameof(CellY), SupportKind.Terrain);

    public ObjectId ObjectId => Kind == SupportKind.Object
        ? _objectId
        : throw WrongKind(nameof(ObjectId), SupportKind.Object);

    public static SupportRef None => default;

    public static SupportRef Terrain(
        ushort mapId,
        int cellX,
        int cellY,
        int topZ) =>
        new(
            SupportKind.Terrain,
            mapId: mapId,
            cellX: cellX,
            cellY: cellY,
            topZ: topZ);

    public static SupportRef Object(ObjectId objectId, int topZ)
    {
        if (objectId.IsNone)
        {
            throw new ArgumentException(
                "An object support requires a live handle.",
                nameof(objectId));
        }

        return new SupportRef(
            SupportKind.Object,
            objectId: objectId,
            topZ: topZ);
    }

    public override string ToString() =>
        Kind switch
        {
            SupportKind.Terrain =>
                $"Terrain(map={_mapId}, cell=({_cellX}, {_cellY}), " +
                $"top={_topZ})",
            SupportKind.Object => $"Object({_objectId}, top={_topZ})",
            _ => "None",
        };

    private InvalidOperationException WrongKind(
        string member,
        params SupportKind[] expected) =>
        new(
            $"{member} does not exist for {Kind}; expected " +
            $"{string.Join(" or ", expected)}.");
}

public enum MotionState : byte
{
    /// <summary>At rest on a support, or held by content that ignores gravity.</summary>
    Resting = 0,

    /// <summary>Unsupported and advanced by gravity on the simulation tick.</summary>
    Falling = 1,
}

/// <summary>
/// A physics field change committed with the object locations of the same
/// transaction, so motion state, velocity and support never disagree with a
/// position for even one revision.
/// </summary>
public readonly record struct ObjectPhysicsUpdate(
    ObjectId ObjectId,
    MotionState Motion,
    int VerticalVelocity,
    SupportRef Support)
{
    public static ObjectPhysicsUpdate Resting(
        ObjectId objectId,
        SupportRef support) =>
        new(objectId, MotionState.Resting, 0, support);

    public static ObjectPhysicsUpdate Falling(
        ObjectId objectId,
        int verticalVelocity) =>
        new(objectId, MotionState.Falling, verticalVelocity, SupportRef.None);

    internal void Validate()
    {
        if (ObjectId.IsNone)
        {
            throw new ArgumentException(
                "A physics update requires a live object handle.");
        }

        if (VerticalVelocity < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(VerticalVelocity),
                "Vertical velocity is a non-negative fall speed.");
        }

        if (Motion == MotionState.Falling && !Support.IsNone)
        {
            throw new ArgumentException(
                "A falling object cannot reference a support.");
        }

        if (Motion == MotionState.Resting && VerticalVelocity != 0)
        {
            throw new ArgumentException(
                "A resting object cannot carry vertical velocity.");
        }
    }
}

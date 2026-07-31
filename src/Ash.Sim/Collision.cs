using Ash.Core;

namespace Ash.Sim;

public enum PlacementBlockerKind : byte
{
    None = 0,
    MapEdge = 1,
    Terrain = 2,
    Object = 3,
}

/// <summary>
/// The exact thing that refused a placement or stopped a sweep. Physical
/// failures name their blocker; they never report "somewhere around here".
/// </summary>
public readonly record struct PlacementBlocker
{
    private readonly ObjectId _objectId;
    private readonly int _terrainX;
    private readonly int _terrainY;

    private PlacementBlocker(
        PlacementBlockerKind kind,
        ObjectId objectId = default,
        int terrainX = 0,
        int terrainY = 0)
    {
        Kind = kind;
        _objectId = objectId;
        _terrainX = terrainX;
        _terrainY = terrainY;
    }

    public PlacementBlockerKind Kind { get; }

    public ObjectId ObjectId => Kind == PlacementBlockerKind.Object
        ? _objectId
        : throw WrongKind(nameof(ObjectId), PlacementBlockerKind.Object);

    public int TerrainX => Kind == PlacementBlockerKind.Terrain
        ? _terrainX
        : throw WrongKind(nameof(TerrainX), PlacementBlockerKind.Terrain);

    public int TerrainY => Kind == PlacementBlockerKind.Terrain
        ? _terrainY
        : throw WrongKind(nameof(TerrainY), PlacementBlockerKind.Terrain);

    public static PlacementBlocker None => default;

    public static PlacementBlocker MapEdge =>
        new(PlacementBlockerKind.MapEdge);

    public static PlacementBlocker Terrain(int x, int y) =>
        new(PlacementBlockerKind.Terrain, terrainX: x, terrainY: y);

    public static PlacementBlocker Object(ObjectId id)
    {
        if (id.IsNone)
        {
            throw new ArgumentException(
                "An object blocker requires a live handle.",
                nameof(id));
        }

        return new PlacementBlocker(PlacementBlockerKind.Object, objectId: id);
    }

    public override string ToString() =>
        Kind switch
        {
            PlacementBlockerKind.MapEdge => "MapEdge",
            PlacementBlockerKind.Terrain =>
                $"Terrain({_terrainX}, {_terrainY})",
            PlacementBlockerKind.Object => $"Object({_objectId})",
            _ => "None",
        };

    private InvalidOperationException WrongKind(
        string member,
        PlacementBlockerKind expected) =>
        new($"{member} does not exist for {Kind}; expected {expected}.");
}

public enum PlacementFailure : byte
{
    None = 0,
    UnknownMap,
    OutOfMapBounds,
    Immovable,
    TerrainBlocked,
    ObjectBlocked,
}

public readonly record struct PlacementResult(
    bool Allowed,
    PlacementFailure Failure,
    PlacementBlocker Blocker,
    string Message)
{
    public static PlacementResult Allow(string message) =>
        new(true, PlacementFailure.None, PlacementBlocker.None, message);

    public static PlacementResult Reject(
        PlacementFailure failure,
        PlacementBlocker blocker,
        string message) =>
        new(false, failure, blocker, message);
}

public enum MovementFailure : byte
{
    None = 0,
    Immovable,
    Blocked,
    StepTooHigh,
}

/// <summary>
/// The outcome of one swept move. The solver never mutates the world: a caller
/// decides whether to commit <see cref="ResolvedPosition"/> through the object
/// transaction.
/// </summary>
public readonly record struct MovementResolution(
    Vec3i ResolvedPosition,
    bool Moved,
    bool ReachedTarget,
    MotionState Motion,
    SupportRef Support,
    MovementFailure Failure,
    PlacementBlocker Blocker,
    string Message)
{
    /// <summary>
    /// The physics fields to commit with the resolved position, so elevation,
    /// support and motion state change in the same transaction as the move.
    /// </summary>
    public ObjectPhysicsUpdate PhysicsFor(ObjectId objectId) =>
        Motion == MotionState.Falling
            ? ObjectPhysicsUpdate.Falling(objectId, 0)
            : ObjectPhysicsUpdate.Resting(objectId, Support);
}

/// <summary>
/// Swept-volume movement resolution shared by actors, monsters and movable
/// props. All sweep arithmetic is checked integer arithmetic: contact times are
/// exact rationals, never floating point.
/// </summary>
public sealed class MovementSolver
{
    private readonly ObjectStore _objects;

    public MovementSolver(ObjectStore objects)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    }

    /// <summary>
    /// Sweeps a move. <paramref name="stepHeight"/> overrides how high this
    /// particular attempt can climb, because vaulting is rolled per attempt
    /// rather than fixed on the body; omitting it uses the mover's own.
    /// </summary>
    public MovementResolution Resolve(
        ObjectId moverId,
        Vec3i displacement,
        int? stepHeight = null)
    {
        if (displacement == Vec3i.Zero)
        {
            throw new ArgumentException(
                "A movement sweep requires a non-zero displacement.",
                nameof(displacement));
        }

        if (stepHeight is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepHeight));
        }

        var mover = _objects.Get(moverId);
        if (mover.Location.Kind != LocationKind.OnMap)
        {
            throw new InvalidOperationException(
                $"Object {moverId} is not on a map and cannot be swept.");
        }

        var origin = mover.Location.Position;
        var map = _objects.Maps.Get(mover.Location.MapId);
        if (mover.HasFlag(ObjectFlags.Fixed))
        {
            return Refused(
                mover,
                MovementFailure.Immovable,
                PlacementBlocker.None,
                $"{mover.Name} is fixed in place.");
        }

        var target = new Vec3i(
            checked(origin.X + displacement.X),
            checked(origin.Y + displacement.Y),
            checked(origin.Z + displacement.Z));
        var reach = stepHeight ?? mover.StepHeight;

        // 1. Resolve the elevation the move happens at, so a step up onto a
        //    stair or platform is swept at the height it will end on.
        var climb = map.FindSupport(At(mover, target), target.Z + reach);
        if (climb.IsNone)
        {
            var anySurface = map.FindSupport(At(mover, target), int.MaxValue);
            if (!anySurface.IsNone)
            {
                return Refused(
                    mover,
                    MovementFailure.StepTooHigh,
                    Blocker(anySurface),
                    $"The step up to {anySurface.TopZ} is higher than " +
                    $"{mover.Name} can climb.");
            }
        }

        var sweepZ = climb.IsNone
            ? target.Z
            : Math.Max(target.Z, climb.TopZ);
        var start = new Vec3i(origin.X, origin.Y, sweepZ);
        var horizontal = new Vec3i(displacement.X, displacement.Y, 0);

        // 2. Sweep horizontally at that elevation.
        var from = SweptBox.From(WorldMap.VolumeFor(At(mover, start)));
        var contact = horizontal == Vec3i.Zero
            ? (Time: Fraction.One, Blocker: PlacementBlocker.None)
            : FirstContact(map, mover, from, horizontal);
        var swept = Advance(start, horizontal, contact.Time);

        // 3. Attach to the support under the position the sweep actually
        //    reached, dropping onto it when that is within one step.
        var support = map.FindSupport(At(mover, swept), sweepZ);
        var motion = MotionState.Resting;
        var resolved = swept;
        if (support.IsNone)
        {
            // Walking off an edge is a legal move: gravity, not the movement
            // solver, decides where the object comes down.
            motion = mover.HasFlag(ObjectFlags.AffectedByGravity)
                ? MotionState.Falling
                : MotionState.Resting;
        }
        else if (sweepZ - support.TopZ <= reach)
        {
            resolved = swept with { Z = support.TopZ };
        }
        else if (mover.HasFlag(ObjectFlags.AffectedByGravity))
        {
            motion = MotionState.Falling;
            support = SupportRef.None;
        }
        else
        {
            support = SupportRef.None;
        }

        var placement = map.ValidatePlacement(
            At(mover, resolved),
            mover.Location);
        if (!placement.Allowed)
        {
            // The rational contact time is exact, but the resolved position is
            // the integer point at or before it. If that point still fails the
            // one authoritative placement contract, the move is refused whole
            // rather than repaired.
            return Refused(
                mover,
                MovementFailure.Blocked,
                placement.Blocker,
                placement.Message);
        }

        var reached = resolved.X == target.X && resolved.Y == target.Y;
        return new MovementResolution(
            resolved,
            Moved: resolved != origin,
            ReachedTarget: reached,
            motion,
            motion == MotionState.Falling ? SupportRef.None : support,
            reached ? MovementFailure.None : MovementFailure.Blocked,
            reached ? PlacementBlocker.None : contact.Blocker,
            reached
                ? $"{mover.Name} moves."
                : Describe(contact.Blocker));
    }

    private static WorldObject At(WorldObject value, Vec3i position) =>
        value with
        {
            Location = ObjectLocation.OnMap(
                value.Location.MapId,
                position),
        };

    private static PlacementBlocker Blocker(SupportRef support) =>
        support.Kind == SupportKind.Object
            ? PlacementBlocker.Object(support.ObjectId)
            : PlacementBlocker.Terrain(support.CellX, support.CellY);

    private static MovementResolution Refused(
        WorldObject mover,
        MovementFailure failure,
        PlacementBlocker blocker,
        string message) =>
        new(
            mover.Location.Position,
            Moved: false,
            ReachedTarget: false,
            mover.Motion,
            mover.Support,
            failure,
            blocker,
            message);

    private (Fraction Time, PlacementBlocker Blocker) FirstContact(
        WorldMap map,
        WorldObject mover,
        SweptBox from,
        Vec3i displacement)
    {
        var earliest = Fraction.One;
        var blocker = PlacementBlocker.None;
        Consider(EdgeContact(map, from, displacement), PlacementBlocker.MapEdge);

        var hull = from.Hull(displacement).ToRectangle();
        foreach (var (x, y, cell) in map.TerrainSpan(hull))
        {
            if (!cell.Flags.HasFlag(TerrainFlags.Solid))
            {
                continue;
            }

            Consider(
                SweptBox.Terrain(x, y, cell, from, displacement)
                    .Contact(from, displacement),
                PlacementBlocker.Terrain(x, y));
        }

        if (mover.HasFlag(ObjectFlags.Solid))
        {
            foreach (var candidate in map.Query(hull, ObjectFlags.Solid))
            {
                if (candidate.Id == mover.Id)
                {
                    continue;
                }

                Consider(
                    SweptBox.From(WorldMap.VolumeFor(candidate))
                        .Contact(from, displacement),
                    PlacementBlocker.Object(candidate.Id));
            }
        }

        return (earliest, blocker);

        void Consider(Fraction? candidate, PlacementBlocker source)
        {
            if (candidate is not { } time || time.CompareTo(earliest) >= 0)
            {
                return;
            }

            earliest = time;
            blocker = source;
        }
    }

    private static Fraction? EdgeContact(
        WorldMap map,
        SweptBox from,
        Vec3i displacement)
    {
        var bounds = map.WorldBounds;
        var limit = Fraction.One;
        var blocked = false;
        Axis(from.XMin, from.XMax, displacement.X, bounds.XMin, bounds.XMax);
        Axis(from.YMin, from.YMax, displacement.Y, bounds.YMin, bounds.YMax);
        return blocked ? limit : null;

        void Axis(long min, long max, long delta, long low, long high)
        {
            if (delta == 0)
            {
                return;
            }

            var allowed = delta > 0
                ? new Fraction(high - max, delta)
                : new Fraction(low - min, delta);
            if (allowed.CompareTo(Fraction.Zero) < 0)
            {
                allowed = Fraction.Zero;
            }

            if (allowed.CompareTo(limit) < 0)
            {
                limit = allowed;
                blocked = true;
            }
        }
    }

    private static Vec3i Advance(Vec3i origin, Vec3i displacement, Fraction time)
    {
        if (time.CompareTo(Fraction.One) >= 0)
        {
            return new Vec3i(
                checked(origin.X + displacement.X),
                checked(origin.Y + displacement.Y),
                checked(origin.Z + displacement.Z));
        }

        return new Vec3i(
            checked(origin.X + (int)time.Scale(displacement.X)),
            checked(origin.Y + (int)time.Scale(displacement.Y)),
            checked(origin.Z + (int)time.Scale(displacement.Z)));
    }

    private string Describe(PlacementBlocker blocker) =>
        blocker.Kind switch
        {
            PlacementBlockerKind.MapEdge => "The map edge blocks the way.",
            PlacementBlockerKind.Terrain =>
                $"Solid terrain at ({blocker.TerrainX}, {blocker.TerrainY}) " +
                "blocks the way.",
            PlacementBlockerKind.Object =>
                $"{_objects.Get(blocker.ObjectId).Name} occupies that space.",
            _ => throw new InvalidOperationException(
                "A stopped sweep must name its blocker."),
        };
}

/// <summary>
/// An exact non-negative rational in [0, 1] used for swept contact times.
/// Comparisons cross-multiply in <see cref="Int128"/> so no product overflows.
/// </summary>
internal readonly record struct Fraction
{
    public Fraction(long numerator, long denominator)
    {
        if (denominator == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator));
        }

        if (denominator < 0)
        {
            numerator = checked(-numerator);
            denominator = checked(-denominator);
        }

        Numerator = numerator;
        Denominator = denominator;
    }

    public long Numerator { get; }

    public long Denominator { get; }

    public static Fraction Zero => new(0, 1);

    public static Fraction One => new(1, 1);

    public int CompareTo(Fraction other) =>
        ((Int128)Numerator * other.Denominator)
        .CompareTo((Int128)other.Numerator * Denominator);

    /// <summary>
    /// The integer part of <paramref name="value"/> scaled by this fraction,
    /// truncated toward zero so the result never passes the contact point.
    /// </summary>
    public long Scale(long value) =>
        checked(value * Numerator) / Denominator;

    public static Fraction Min(Fraction left, Fraction right) =>
        left.CompareTo(right) <= 0 ? left : right;

    public static Fraction Max(Fraction left, Fraction right) =>
        left.CompareTo(right) >= 0 ? left : right;
}

/// <summary>
/// A 64-bit axis-aligned world box with half-open bounds, used for swept
/// collision so touching faces are never penetration.
/// </summary>
internal readonly record struct SweptBox(
    long XMin,
    long XMax,
    long YMin,
    long YMax,
    long ZMin,
    long ZMax)
{
    public static SweptBox From(WorldVolume volume) =>
        new(
            volume.Horizontal.XMin,
            volume.Horizontal.XMax,
            volume.Horizontal.YMin,
            volume.Horizontal.YMax,
            volume.ZMin,
            volume.ZMax);

    public static SweptBox Terrain(
        int x,
        int y,
        TerrainCell cell,
        SweptBox mover,
        Vec3i displacement)
    {
        var tile = (long)WorldMap.WorldUnitsPerTile;
        var top = Math.Max(mover.ZMax, mover.ZMax + displacement.Z) + 1;
        return new SweptBox(
            x * tile,
            (x + 1) * tile,
            y * tile,
            (y + 1) * tile,
            cell.FloorZ,
            Math.Max(top, (long)cell.FloorZ + 1));
    }

    public SweptBox Hull(Vec3i displacement) =>
        new(
            Math.Min(XMin, XMin + displacement.X),
            Math.Max(XMax, XMax + displacement.X),
            Math.Min(YMin, YMin + displacement.Y),
            Math.Max(YMax, YMax + displacement.Y),
            Math.Min(ZMin, ZMin + displacement.Z),
            Math.Max(ZMax, ZMax + displacement.Z));

    public WorldRectangle ToRectangle() =>
        new(
            checked((int)XMin),
            checked((int)XMax),
            checked((int)YMin),
            checked((int)YMax));

    /// <summary>
    /// The first fraction of <paramref name="displacement"/> at which
    /// <paramref name="mover"/> penetrates this box, or null when the sweep
    /// never overlaps it.
    /// </summary>
    public Fraction? Contact(SweptBox mover, Vec3i displacement)
    {
        var enter = Fraction.Zero;
        var exit = Fraction.One;
        if (!Axis(mover.XMin, mover.XMax, XMin, XMax, displacement.X) ||
            !Axis(mover.YMin, mover.YMax, YMin, YMax, displacement.Y) ||
            !Axis(mover.ZMin, mover.ZMax, ZMin, ZMax, displacement.Z))
        {
            return null;
        }

        return enter.CompareTo(exit) < 0 ? enter : null;

        bool Axis(long min, long max, long otherMin, long otherMax, long delta)
        {
            if (delta == 0)
            {
                return min < otherMax && max > otherMin;
            }

            var first = new Fraction(otherMin - max, delta);
            var second = new Fraction(otherMax - min, delta);
            var axisEnter = Fraction.Min(first, second);
            var axisExit = Fraction.Max(first, second);
            enter = Fraction.Max(enter, axisEnter);
            exit = Fraction.Min(exit, axisExit);
            return true;
        }
    }
}

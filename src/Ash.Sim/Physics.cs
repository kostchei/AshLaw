using Ash.Core;

namespace Ash.Sim;

public enum PhysicsEventKind : byte
{
    /// <summary>An object lost its support and started falling.</summary>
    FallStarted = 0,

    /// <summary>An object came to rest on a support.</summary>
    Landed = 1,
}

/// <summary>
/// A world event published after the physics commit that caused it. Fall
/// distance and landing speed are recorded for later rules; no damage is
/// applied here, because the environmental fall table is still unvalidated.
/// </summary>
public readonly record struct PhysicsEvent(
    PhysicsEventKind Kind,
    ObjectId ObjectId,
    int FallDistance,
    int LandingSpeed,
    SupportRef Support);

public readonly record struct PhysicsTickResult(
    long Tick,
    IReadOnlyList<PhysicsEvent> Events);

public enum CarryFailure : byte
{
    None = 0,
    Blocked,
}

/// <summary>
/// The result of moving a support with everything resting on it.
/// </summary>
public readonly record struct CarryResult(
    bool Succeeded,
    CarryFailure Failure,
    IReadOnlyList<ObjectId> Carried,
    PlacementBlocker Blocker,
    string Message);

/// <summary>
/// Support maintenance and gravity on the fixed simulation tick. Every change
/// it makes goes through one <see cref="ObjectTransferService"/> transaction, so
/// positions, motion state and the support graph are never briefly disagreeing.
/// </summary>
public sealed class PhysicsSystem
{
    public const int DefaultGravityPerTick = 4;
    public const int DefaultTerminalVelocity = 64;

    private readonly ObjectStore _objects;
    private readonly ObjectTransferService _transfers;
    private readonly MovementSolver _movement;

    public PhysicsSystem(
        ObjectStore objects,
        int gravityPerTick = DefaultGravityPerTick,
        int terminalVelocity = DefaultTerminalVelocity,
        long startTick = 0)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        if (gravityPerTick <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gravityPerTick));
        }

        if (terminalVelocity < gravityPerTick)
        {
            throw new ArgumentOutOfRangeException(nameof(terminalVelocity));
        }

        _transfers = new ObjectTransferService(objects);
        _movement = new MovementSolver(objects);
        if (startTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startTick));
        }

        GravityPerTick = gravityPerTick;
        TerminalVelocity = terminalVelocity;

        // A loaded world resumes on the tick it was saved on: the tick counter
        // is authoritative sequence state, not a fresh counter per session.
        Tick = startTick;
    }

    public int GravityPerTick { get; }

    public int TerminalVelocity { get; }

    public long Tick { get; private set; }

    /// <summary>
    /// Whether a tick is mid-flight. The save boundary waits for this to clear
    /// so a snapshot never lands between two commits of the same tick.
    /// </summary>
    public bool IsAdvancing { get; private set; }

    /// <summary>
    /// Advances one fixed simulation tick: revalidate supports, then move every
    /// falling object. Objects are processed in <see cref="ObjectId"/> order and
    /// each change is one transaction, so repeating a tick sequence reproduces
    /// identical positions, supports and events.
    /// </summary>
    public PhysicsTickResult Advance()
    {
        IsAdvancing = true;
        try
        {
            return AdvanceTick();
        }
        finally
        {
            IsAdvancing = false;
        }
    }

    private PhysicsTickResult AdvanceTick()
    {
        var events = new List<PhysicsEvent>();
        foreach (var id in GravityObjects())
        {
            if (!_objects.TryGet(id, out var value) ||
                value.Location.Kind != LocationKind.OnMap)
            {
                // Destroyed, contained or teleported since the tick began: the
                // old fall is cancelled rather than applied to a stale handle.
                continue;
            }

            var map = _objects.Maps.Get(value.Location.MapId);
            if (value.Motion == MotionState.Resting)
            {
                MaintainSupport(map, value, events);
                continue;
            }

            Fall(map, value, events);
        }

        Tick++;
        return new PhysicsTickResult(Tick, events);
    }

    /// <summary>
    /// Moves a support and everything resting on it, transitively, in one
    /// projected commit. If a carried object would be embedded, the whole move
    /// is refused and the blocker reported: v1 blocks the support.
    /// </summary>
    public CarryResult MoveWithDependants(
        ObjectId supporterId,
        Vec3i displacement)
    {
        var supporter = _objects.Get(supporterId);
        var resolution = _movement.Resolve(supporterId, displacement);
        if (!resolution.Moved)
        {
            return new CarryResult(
                false,
                CarryFailure.Blocked,
                [],
                resolution.Blocker,
                resolution.Message);
        }

        var map = _objects.Maps.Get(supporter.Location.MapId);
        var carried = Dependants(map, supporterId);
        var delta = new Vec3i(
            resolution.ResolvedPosition.X - supporter.Location.Position.X,
            resolution.ResolvedPosition.Y - supporter.Location.Position.Y,
            resolution.ResolvedPosition.Z - supporter.Location.Position.Z);
        var requests = new List<ObjectTransferRequest>
        {
            new(
                supporterId,
                supporter.Location,
                ObjectLocation.OnMap(
                    map.MapId,
                    resolution.ResolvedPosition)),
        };
        var physics = new List<ObjectPhysicsUpdate>
        {
            resolution.PhysicsFor(supporterId),
        };
        foreach (var id in carried)
        {
            var value = _objects.Get(id);
            var moved = Offset(value.Location.Position, delta);
            requests.Add(
                new ObjectTransferRequest(
                    id,
                    value.Location,
                    ObjectLocation.OnMap(map.MapId, moved)));
            physics.Add(
                ObjectPhysicsUpdate.Resting(
                    id,
                    SupportRef.Object(
                        value.Support.ObjectId,
                        checked(value.Support.TopZ + delta.Z))));
        }

        var result = _transfers.Execute(requests, physics);
        return result.Succeeded
            ? new CarryResult(
                true,
                CarryFailure.None,
                carried,
                PlacementBlocker.None,
                $"{supporter.Name} moves with {carried.Count} carried " +
                "objects.")
            : new CarryResult(
                false,
                CarryFailure.Blocked,
                carried,
                result.Blocker,
                result.Message);
    }

    /// <summary>
    /// The physical invariants that must hold after every physics commit.
    /// </summary>
    public void ValidateInvariants()
    {
        foreach (var value in _objects.Enumerate())
        {
            if (value.Location.Kind != LocationKind.OnMap)
            {
                if (!value.Support.IsNone ||
                    value.Motion != MotionState.Resting)
                {
                    throw new InvalidOperationException(
                        $"Contained object {value.Id} still holds map physics " +
                        "state.");
                }

                continue;
            }

            var map = _objects.Maps.Get(value.Location.MapId);
            var volume = WorldMap.VolumeFor(value);
            if (value.Motion == MotionState.Falling)
            {
                if (!value.Support.IsNone)
                {
                    throw new InvalidOperationException(
                        $"Falling object {value.Id} references a support.");
                }

                if (value.HasFlag(ObjectFlags.Fixed))
                {
                    throw new InvalidOperationException(
                        $"Fixed object {value.Id} is falling.");
                }
            }

            if (!value.Support.IsNone)
            {
                if (value.Support.TopZ != volume.ZMin)
                {
                    throw new InvalidOperationException(
                        $"Object {value.Id} rests at {volume.ZMin} on a " +
                        $"support whose top is {value.Support.TopZ}.");
                }

                if (value.Support.Kind == SupportKind.Object)
                {
                    var supporter = _objects.Get(value.Support.ObjectId);
                    if (!supporter.CanSupport ||
                        !map.SupportedObjects(value.Support.ObjectId)
                            .Contains(value.Id))
                    {
                        throw new InvalidOperationException(
                            $"Object {value.Id} references an invalid support " +
                            $"{value.Support.ObjectId}.");
                    }
                }
            }
            else if (value.HasFlag(ObjectFlags.AffectedByGravity) &&
                     value.Motion == MotionState.Resting)
            {
                throw new InvalidOperationException(
                    $"Grounded object {value.Id} has no support.");
            }

            var placement = map.ValidatePlacement(value, value.Location);
            if (!placement.Allowed &&
                placement.Failure != PlacementFailure.Immovable)
            {
                throw new InvalidOperationException(
                    $"Object {value.Id} is embedded: {placement.Message}");
            }
        }
    }

    private void MaintainSupport(
        WorldMap map,
        WorldObject value,
        List<PhysicsEvent> events)
    {
        if (!value.HasFlag(ObjectFlags.AffectedByGravity))
        {
            return;
        }

        var baseZ = value.Location.Position.Z;
        var support = map.FindSupport(value, baseZ);
        if (!support.IsNone && support.TopZ == baseZ)
        {
            if (support != value.Support)
            {
                Commit(
                    [],
                    [ObjectPhysicsUpdate.Resting(value.Id, support)]);
            }

            return;
        }

        if (value.HasFlag(ObjectFlags.Fixed))
        {
            return;
        }

        Commit([], [ObjectPhysicsUpdate.Falling(value.Id, 0)]);
        events.Add(
            new PhysicsEvent(
                PhysicsEventKind.FallStarted,
                value.Id,
                FallDistance: 0,
                LandingSpeed: 0,
                SupportRef.None));
    }

    private void Fall(
        WorldMap map,
        WorldObject value,
        List<PhysicsEvent> events)
    {
        var speed = Math.Min(
            checked(value.VerticalVelocity + GravityPerTick),
            TerminalVelocity);
        var origin = value.Location.Position;
        var toZ = checked(origin.Z - speed);

        // One swept query per tick: a fast object can never pass through a thin
        // support, because the landing surface is the highest one crossed.
        var support = map.FindSupport(value, origin.Z);
        var lands = !support.IsNone && support.TopZ >= toZ;
        var position = origin with { Z = lands ? support.TopZ : toZ };
        var requests = new List<ObjectTransferRequest>();
        if (position != origin)
        {
            requests.Add(
                new ObjectTransferRequest(
                    value.Id,
                    value.Location,
                    ObjectLocation.OnMap(map.MapId, position)));
        }

        var physics = lands
            ? ObjectPhysicsUpdate.Resting(value.Id, support)
            : ObjectPhysicsUpdate.Falling(value.Id, speed);
        Commit(requests, [physics]);
        if (lands)
        {
            events.Add(
                new PhysicsEvent(
                    PhysicsEventKind.Landed,
                    value.Id,
                    checked(origin.Z - support.TopZ),
                    speed,
                    support));
        }
    }

    private void Commit(
        IReadOnlyList<ObjectTransferRequest> requests,
        IReadOnlyList<ObjectPhysicsUpdate> physics)
    {
        var result = _transfers.Execute(requests, physics);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"A physics commit was rejected: {result.Message}");
        }
    }

    private IReadOnlyList<ObjectId> GravityObjects() =>
        _objects.Enumerate()
            .Where(value =>
                value.Location.Kind == LocationKind.OnMap &&
                (value.HasFlag(ObjectFlags.AffectedByGravity) ||
                 value.Motion == MotionState.Falling))
            .Select(value => value.Id)
            .Order()
            .ToArray();

    private IReadOnlyList<ObjectId> Dependants(
        WorldMap map,
        ObjectId supporter)
    {
        var ordered = new SortedSet<ObjectId>();
        var pending = new Queue<ObjectId>();
        pending.Enqueue(supporter);
        while (pending.TryDequeue(out var current))
        {
            foreach (var dependant in map.SupportedObjects(current))
            {
                if (ordered.Add(dependant))
                {
                    pending.Enqueue(dependant);
                }
            }
        }

        return ordered.ToArray();
    }

    private static Vec3i Offset(Vec3i position, Vec3i delta) =>
        new(
            checked(position.X + delta.X),
            checked(position.Y + delta.Y),
            checked(position.Z + delta.Z));
}

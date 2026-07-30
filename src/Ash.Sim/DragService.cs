using Ash.Core;

namespace Ash.Sim;

public enum DragFailure : byte
{
    None = 0,
    AlreadyDragging,
    NotDragging,
    StaleObject,
    Immovable,
    OutOfReach,
    NoSurface,
    Rejected,

    /// <summary>
    /// A cancel could not put the object back where it came from, so the drag
    /// is still live and the caller must drop it somewhere valid.
    /// </summary>
    SourceLost,
}

public readonly record struct DragResult(
    bool Succeeded,
    DragFailure Failure,
    string Message,
    PlacementBlocker Blocker)
{
    public static DragResult Ok(string message) =>
        new(true, DragFailure.None, message, PlacementBlocker.None);

    public static DragResult Reject(
        DragFailure failure,
        string message,
        PlacementBlocker blocker = default) =>
        new(false, failure, message, blocker);
}

/// <summary>
/// The live drag, if there is one. The dragged object really is
/// <see cref="LocationKind.InTransfer"/> in the store for the whole gesture, so
/// it leaves the map index, no query returns it, and the save boundary defers
/// until the gesture ends.
/// </summary>
public readonly record struct DragState(
    bool IsActive,
    ObjectId ObjectId,
    ObjectId Holder,
    ObjectLocation Source,
    uint TransferId,
    ObjectPhysicsUpdate SourcePhysics)
{
    public static DragState None => default;
}

/// <summary>
/// One drag state machine for every kind of pick-up and drop. Selection decides
/// <em>what</em> is dragged; this decides whether the gesture is legal and
/// commits it through the same object transaction and physical placement
/// contract that movement and gravity use. There is no second inventory model
/// and no "move it and repair it afterwards" path.
/// </summary>
public sealed class DragService
{
    /// <summary>Two tiles: what an actor can reach without moving.</summary>
    public const int DefaultReachUnits = 2 * WorldMap.WorldUnitsPerTile;

    private readonly ObjectStore _objects;
    private readonly ObjectTransferService _transfers;
    private uint _nextTransferId = 1;

    public DragService(ObjectStore objects, int reachUnits = DefaultReachUnits)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        if (reachUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reachUnits));
        }

        _transfers = new ObjectTransferService(objects);
        ReachUnits = reachUnits;
    }

    public int ReachUnits { get; }

    public DragState State { get; private set; } = DragState.None;

    public bool IsDragging => State.IsActive;

    /// <summary>
    /// Picks an object up. The object must be live, movable and within the
    /// holder's reach; anything the holder already carries is always in reach.
    /// </summary>
    public DragResult Begin(ObjectId holder, ObjectId target)
    {
        if (State.IsActive)
        {
            return DragResult.Reject(
                DragFailure.AlreadyDragging,
                $"{Name(State.ObjectId)} is already in hand.");
        }

        if (!_objects.TryGet(holder, out var holderValue) ||
            !_objects.TryGet(target, out var value))
        {
            return DragResult.Reject(
                DragFailure.StaleObject,
                "That object is no longer there.");
        }

        if (target == holder)
        {
            return DragResult.Reject(
                DragFailure.Immovable,
                $"{holderValue.Name} cannot pick itself up.");
        }

        if (value.HasFlag(ObjectFlags.Fixed) ||
            !value.HasFlag(ObjectFlags.Movable))
        {
            return DragResult.Reject(
                DragFailure.Immovable,
                $"{value.Name} cannot be moved.");
        }

        var reach = CheckReach(holderValue, value.Location, value.Name);
        if (reach is { } failure)
        {
            return failure;
        }

        var transferId = _nextTransferId++;
        var source = value.Location;
        var transfer = _transfers.Execute(
            new ObjectTransferRequest(
                target,
                source,
                ObjectLocation.InTransfer(transferId)));
        if (!transfer.Succeeded)
        {
            return DragResult.Reject(
                DragFailure.Rejected,
                transfer.Message,
                transfer.Blocker);
        }

        // Cancelling is an undo, so the physics state the object had before the
        // gesture is captured here rather than re-derived afterwards.
        State = new DragState(
            true,
            target,
            holder,
            source,
            transferId,
            new ObjectPhysicsUpdate(
                target,
                value.Motion,
                value.VerticalVelocity,
                value.Support));
        return DragResult.Ok($"Holding {value.Name}.");
    }

    /// <summary>
    /// Drops the dragged object onto a map position. The elevation is not the
    /// caller's to choose: the highest support under the drop point at or below
    /// the holder's reach decides it, exactly as gravity would.
    /// </summary>
    public DragResult DropOnMap(ushort mapId, int x, int y)
    {
        if (!State.IsActive)
        {
            return NotDragging();
        }

        if (!_objects.Maps.TryGet(mapId, out var map))
        {
            return DragResult.Reject(
                DragFailure.Rejected,
                $"Map {mapId} is not part of this world.");
        }

        var value = _objects.Get(State.ObjectId);
        var holder = _objects.Get(State.Holder);
        var reach = CheckReach(
            holder,
            ObjectLocation.OnMap(mapId, new Vec3i(x, y, 0)),
            value.Name,
            ignoreElevation: true);
        if (reach is { } failure)
        {
            return failure;
        }

        var ceiling = holder.Location.Kind == LocationKind.OnMap
            ? checked(holder.Location.Position.Z + holder.Height)
            : int.MaxValue;
        var probe = value with
        {
            Location = ObjectLocation.OnMap(mapId, new Vec3i(x, y, ceiling)),
        };
        var support = map.FindSupport(probe, ceiling);
        if (support.IsNone)
        {
            return DragResult.Reject(
                DragFailure.NoSurface,
                $"There is nothing to put {value.Name} on there.");
        }

        var destination = ObjectLocation.OnMap(
            mapId,
            new Vec3i(x, y, support.TopZ));
        return Commit(
            destination,
            ObjectPhysicsUpdate.Resting(State.ObjectId, support),
            $"Put {value.Name} down.");
    }

    public DragResult DropInContainer(ObjectId container)
    {
        if (!State.IsActive)
        {
            return NotDragging();
        }

        if (!_objects.TryGet(container, out var target))
        {
            return DragResult.Reject(
                DragFailure.StaleObject,
                "That container is no longer there.");
        }

        var holder = _objects.Get(State.Holder);
        var reach = CheckReach(holder, target.Location, target.Name);
        if (reach is { } failure)
        {
            return failure;
        }

        return Commit(
            ObjectLocation.InContainer(container),
            physics: null,
            $"Put {Name(State.ObjectId)} in {target.Name}.");
    }

    public DragResult DropOnEquipment(ObjectId actor, EquipmentSlot slot) =>
        DropOnEquipment(actor, (byte)slot);

    public DragResult DropOnEquipment(ObjectId actor, byte slot)
    {
        if (!State.IsActive)
        {
            return NotDragging();
        }

        if (!_objects.TryGet(actor, out var wearer))
        {
            return DragResult.Reject(
                DragFailure.StaleObject,
                "That actor is no longer there.");
        }

        var holder = _objects.Get(State.Holder);
        var reach = CheckReach(holder, wearer.Location, wearer.Name);
        if (reach is { } failure)
        {
            return failure;
        }

        return Commit(
            ObjectLocation.Equipped(actor, slot),
            physics: null,
            $"{wearer.Name} equips {Name(State.ObjectId)}.");
    }

    /// <summary>
    /// Puts the object back exactly where the drag started. If the world has
    /// moved on and the source is no longer valid, the drag stays live and says
    /// so rather than dropping the object somewhere nobody asked for.
    /// </summary>
    public DragResult Cancel()
    {
        if (!State.IsActive)
        {
            return NotDragging();
        }

        var name = Name(State.ObjectId);
        var source = State.Source;
        var physics = source.Kind == LocationKind.OnMap
            ? RestorablePhysics(source)
            : null;
        var restored = _transfers.Execute(
            [
                new ObjectTransferRequest(
                    State.ObjectId,
                    ObjectLocation.InTransfer(State.TransferId),
                    source),
            ],
            physics is null ? [] : [physics.Value]);
        if (!restored.Succeeded)
        {
            return DragResult.Reject(
                DragFailure.SourceLost,
                $"{name} cannot go back where it came from: {restored.Message}",
                restored.Blocker);
        }

        State = DragState.None;
        return DragResult.Ok($"Put {name} back.");
    }

    /// <summary>
    /// The physics state to put back on a cancel: what the object had when the
    /// drag began, unless the world has since invalidated it — a destroyed
    /// supporter, say — in which case whatever now holds that spot.
    /// </summary>
    private ObjectPhysicsUpdate? RestorablePhysics(ObjectLocation location)
    {
        if (!_objects.Maps.TryGet(location.MapId, out var map))
        {
            return null;
        }

        var captured = State.SourcePhysics;
        if (captured.Support.Kind != SupportKind.Object ||
            _objects.TryGet(captured.Support.ObjectId, out _))
        {
            return captured;
        }

        var value = _objects.Get(State.ObjectId) with { Location = location };
        var support = map.FindSupport(value, location.Position.Z);
        return support.IsNone || support.TopZ != location.Position.Z
            ? ObjectPhysicsUpdate.Falling(State.ObjectId, 0)
            : ObjectPhysicsUpdate.Resting(State.ObjectId, support);
    }

    private DragResult Commit(
        ObjectLocation destination,
        ObjectPhysicsUpdate? physics,
        string message)
    {
        var transfer = _transfers.Execute(
            [
                new ObjectTransferRequest(
                    State.ObjectId,
                    ObjectLocation.InTransfer(State.TransferId),
                    destination),
            ],
            physics is null ? [] : [physics.Value]);
        if (!transfer.Succeeded)
        {
            // The drag survives a refused drop: the object is still in hand and
            // the caller can try somewhere else or cancel.
            return DragResult.Reject(
                DragFailure.Rejected,
                transfer.Message,
                transfer.Blocker);
        }

        State = DragState.None;
        return DragResult.Ok(message);
    }

    /// <summary>
    /// Whether <paramref name="holder"/> can reach <paramref name="location"/>.
    /// Anything the holder carries or wears is reachable by definition; anything
    /// on a map is reachable within <see cref="ReachUnits"/> horizontally.
    /// </summary>
    private DragResult? CheckReach(
        WorldObject holder,
        ObjectLocation location,
        string name,
        bool ignoreElevation = false)
    {
        switch (location.Kind)
        {
            case LocationKind.InContainer:
            case LocationKind.Equipped:
                return Owns(holder.Id, location.Parent)
                    ? null
                    : CheckReach(
                        holder,
                        _objects.Get(location.Parent).Location,
                        name,
                        ignoreElevation);
            case LocationKind.InTransfer:
                return DragResult.Reject(
                    DragFailure.Rejected,
                    $"{name} is already being moved.");
            case LocationKind.OnMap:
                break;
            default:
                return DragResult.Reject(
                    DragFailure.StaleObject,
                    $"{name} is nowhere.");
        }

        if (holder.Location.Kind != LocationKind.OnMap ||
            holder.Location.MapId != location.MapId)
        {
            return DragResult.Reject(
                DragFailure.OutOfReach,
                $"{name} is not on the same map.");
        }

        var from = holder.Location.Position;
        var to = location.Position;
        var distance = Math.Max(
            Math.Abs((long)from.X - to.X),
            Math.Abs((long)from.Y - to.Y));
        if (distance > ReachUnits)
        {
            return DragResult.Reject(
                DragFailure.OutOfReach,
                $"{name} is out of reach.");
        }

        if (!ignoreElevation &&
            Math.Abs((long)from.Z - to.Z) > ReachUnits)
        {
            return DragResult.Reject(
                DragFailure.OutOfReach,
                $"{name} is out of reach.");
        }

        return null;
    }

    /// <summary>Whether <paramref name="parent"/> is the holder or is carried by them.</summary>
    private bool Owns(ObjectId holder, ObjectId parent)
    {
        var current = parent;
        var guard = 0;
        while (!current.IsNone && guard++ < 64)
        {
            if (current == holder)
            {
                return true;
            }

            var location = _objects.Get(current).Location;
            current = location.Kind is
                LocationKind.InContainer or LocationKind.Equipped
                ? location.Parent
                : ObjectId.None;
        }

        return false;
    }

    private static DragResult NotDragging() =>
        DragResult.Reject(DragFailure.NotDragging, "Nothing is in hand.");

    private string Name(ObjectId id) =>
        _objects.TryGet(id, out var value) ? value.Name : "It";
}

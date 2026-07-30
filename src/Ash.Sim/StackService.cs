namespace Ash.Sim;

public enum StackFailure : byte
{
    None = 0,
    StaleObject,
    NotStackable,

    /// <summary>The two stacks are not the same goods.</summary>
    Incompatible,

    /// <summary>The requested count is not part of the source stack.</summary>
    InvalidCount,

    /// <summary>The destination cannot hold the objects being moved.</summary>
    Rejected,
}

public readonly record struct StackResult(
    bool Succeeded,
    StackFailure Failure,
    string Message,
    ObjectId Stack,
    int Moved,
    int Remaining)
{
    public static StackResult Reject(StackFailure failure, string message) =>
        new(false, failure, message, ObjectId.None, 0, 0);
}

/// <summary>
/// Stack compatibility, merge, split and partial transfer. Quantities live on
/// the object that carries them, so every operation here is one commit through
/// the object store: nothing is ever briefly duplicated or briefly missing.
/// </summary>
public sealed class StackService
{
    private readonly ObjectStore _objects;
    private readonly ObjectTransferService _transfers;

    public StackService(ObjectStore objects)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _transfers = new ObjectTransferService(objects);
    }

    /// <summary>
    /// Whether two live objects are the same goods and may share one handle.
    /// Identity is the authored type and shape plus the state a player can tell
    /// apart: quality and condition. A container is never compatible, because
    /// its contents are part of what it is.
    /// </summary>
    public static bool AreCompatible(WorldObject left, WorldObject right) =>
        left.Id != right.Id &&
        left.HasFlag(ObjectFlags.Stackable) &&
        right.HasFlag(ObjectFlags.Stackable) &&
        !left.HasFlag(ObjectFlags.Container) &&
        !right.HasFlag(ObjectFlags.Container) &&
        left.MaxQuantity > 1 &&
        right.MaxQuantity > 1 &&
        string.Equals(left.TypeId, right.TypeId, StringComparison.Ordinal) &&
        string.Equals(left.ShapeId, right.ShapeId, StringComparison.Ordinal) &&
        left.FrameId == right.FrameId &&
        left.Quality == right.Quality &&
        left.Condition == right.Condition &&
        left.EquipmentSlots == right.EquipmentSlots;

    /// <summary>
    /// The stack in <paramref name="location"/> that
    /// <paramref name="candidate"/> could join, if there is one, preferring the
    /// lowest <see cref="ObjectId"/> so a merge target never depends on
    /// iteration order.
    /// </summary>
    public ObjectId FindMergeTarget(
        WorldObject candidate,
        ObjectLocation location)
    {
        var best = ObjectId.None;
        foreach (var value in _objects.Enumerate())
        {
            if (value.Location != location ||
                !AreCompatible(candidate, value) ||
                value.Quantity >= value.MaxQuantity)
            {
                continue;
            }

            if (best.IsNone || value.Id.CompareTo(best) < 0)
            {
                best = value.Id;
            }
        }

        return best;
    }

    /// <summary>
    /// Pours <paramref name="source"/> into <paramref name="destination"/>,
    /// as far as the destination's stack limit allows. A source emptied by the
    /// merge is destroyed in the same commit; a partial merge leaves the
    /// remainder where it was.
    /// </summary>
    public StackResult Merge(ObjectId source, ObjectId destination)
    {
        if (!_objects.TryGet(source, out var from) ||
            !_objects.TryGet(destination, out var into))
        {
            return StackResult.Reject(
                StackFailure.StaleObject,
                "One of those stacks is no longer there.");
        }

        if (!AreCompatible(from, into))
        {
            return StackResult.Reject(
                StackFailure.Incompatible,
                $"{from.Name} and {into.Name} do not stack together.");
        }

        var space = into.MaxQuantity - into.Quantity;
        if (space <= 0)
        {
            return StackResult.Reject(
                StackFailure.Rejected,
                $"{into.Name} already holds {into.Quantity}.");
        }

        var moved = Math.Min(space, from.Quantity);
        var remaining = from.Quantity - moved;
        var quantities = new List<ObjectQuantityUpdate>
        {
            new(destination, into.Quantity + moved),
        };
        var destroy = new List<ObjectId>();
        if (remaining > 0)
        {
            quantities.Add(new ObjectQuantityUpdate(source, remaining));
        }
        else
        {
            destroy.Add(source);
        }

        _objects.CommitStackChange(quantities, destroy, create: null);
        return new StackResult(
            true,
            StackFailure.None,
            remaining > 0
                ? $"Moved {moved} to {into.Name}; {remaining} left over."
                : $"Merged {moved} into {into.Name}.",
            destination,
            moved,
            remaining);
    }

    /// <summary>
    /// Splits <paramref name="count"/> off <paramref name="source"/> into a new
    /// stack at <paramref name="destination"/>. The split is validated against
    /// the same transfer rules as any other arrival before either side changes.
    /// </summary>
    public StackResult Split(
        ObjectId source,
        int count,
        ObjectLocation destination)
    {
        if (!_objects.TryGet(source, out var from))
        {
            return StackResult.Reject(
                StackFailure.StaleObject,
                "That stack is no longer there.");
        }

        if (!from.HasFlag(ObjectFlags.Stackable) || from.MaxQuantity <= 1)
        {
            return StackResult.Reject(
                StackFailure.NotStackable,
                $"{from.Name} does not come apart.");
        }

        if (count < 1 || count >= from.Quantity)
        {
            return StackResult.Reject(
                StackFailure.InvalidCount,
                $"Cannot split {count} from a stack of {from.Quantity}.");
        }

        var spawn = SpawnLike(from, count, destination);
        var rejection = ValidateArrival(spawn, destination, from.Name);
        if (rejection is { } failure)
        {
            return failure;
        }

        var created = _objects.CommitStackChange(
            [new ObjectQuantityUpdate(source, from.Quantity - count)],
            destroy: [],
            spawn);
        return new StackResult(
            true,
            StackFailure.None,
            $"Split {count} from {from.Name}.",
            created,
            count,
            from.Quantity - count);
    }

    /// <summary>
    /// Moves <paramref name="count"/> of a stack to <paramref name="destination"/>:
    /// the whole stack when the count is all of it, otherwise a split, and a
    /// merge when compatible goods are already there. One call covers "drag the
    /// lot", "drag five" and "top up the pile".
    /// </summary>
    public StackResult TransferQuantity(
        ObjectId source,
        int count,
        ObjectLocation destination)
    {
        if (!_objects.TryGet(source, out var from))
        {
            return StackResult.Reject(
                StackFailure.StaleObject,
                "That stack is no longer there.");
        }

        if (count < 1 || count > from.Quantity)
        {
            return StackResult.Reject(
                StackFailure.InvalidCount,
                $"Cannot move {count} of {from.Quantity}.");
        }

        var target = FindMergeTarget(from, destination);
        if (count == from.Quantity)
        {
            if (target.IsNone)
            {
                var transfer = _transfers.Execute(
                    new ObjectTransferRequest(source, from.Location, destination));
                return transfer.Succeeded
                    ? new StackResult(
                        true,
                        StackFailure.None,
                        $"Moved {from.Name}.",
                        source,
                        count,
                        0)
                    : StackResult.Reject(
                        StackFailure.Rejected,
                        transfer.Message);
            }

            return Merge(source, target);
        }

        if (!target.IsNone)
        {
            // Split first, then pour the new stack into the pile already there,
            // so a partial transfer never leaves two half stacks side by side.
            var split = Split(source, count, from.Location);
            return split.Succeeded ? Merge(split.Stack, target) : split;
        }

        return Split(source, count, destination);
    }

    private StackResult? ValidateArrival(
        ObjectSpawn spawn,
        ObjectLocation destination,
        string name)
    {
        switch (destination.Kind)
        {
            case LocationKind.InContainer:
                if (!_objects.TryGet(destination.Parent, out var container) ||
                    !container.HasFlag(ObjectFlags.Container))
                {
                    return StackResult.Reject(
                        StackFailure.Rejected,
                        "That is not a container.");
                }

                var projected = _objects.GetContents(destination.Parent)
                    .Select(_objects.Get)
                    .Append(spawn.AsProbe(destination))
                    .ToArray();
                if (GearSlots.UsedBy(projected) > container.CarryCapacity)
                {
                    return StackResult.Reject(
                        StackFailure.Rejected,
                        $"{container.Name} is full.");
                }

                return null;
            case LocationKind.OnMap:
                if (!_objects.Maps.TryGet(destination.MapId, out var map))
                {
                    return StackResult.Reject(
                        StackFailure.Rejected,
                        $"Map {destination.MapId} is not part of this world.");
                }

                var placement = map.ValidatePlacement(
                    spawn.AsProbe(destination),
                    destination);
                return placement.Allowed
                    ? null
                    : StackResult.Reject(
                        StackFailure.Rejected,
                        placement.Message);
            case LocationKind.Equipped:
                return StackResult.Reject(
                    StackFailure.Rejected,
                    $"{name} cannot be split straight into an equipment slot.");
            default:
                return StackResult.Reject(
                    StackFailure.Rejected,
                    $"{name} cannot be split to that location.");
        }
    }

    private static ObjectSpawn SpawnLike(
        WorldObject source,
        int quantity,
        ObjectLocation destination) =>
        new()
        {
            TypeId = source.TypeId,
            Name = source.Name,
            ShapeId = source.ShapeId,
            FrameId = source.FrameId,
            Location = destination,
            Footprint = source.Footprint,
            Height = source.Height,
            StepHeight = source.StepHeight,
            Flags = source.Flags,
            EquipmentSlots = source.EquipmentSlots,
            Quality = source.Quality,
            Quantity = quantity,
            MaxQuantity = source.MaxQuantity,
            QuantityPerSlot = source.QuantityPerSlot,
            SlotGroup = source.SlotGroup,
            Condition = source.Condition,
            SlotCost = source.SlotCost,
            SlotCapacity = source.SlotCapacity,
            Strength = source.Strength,
            GearSlotBonus = source.GearSlotBonus,
            Health = source.Health,
            MaxHealth = source.MaxHealth,
        };
}

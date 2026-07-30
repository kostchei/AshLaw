namespace Ash.Sim;

public readonly record struct ObjectTransferRequest(
    ObjectId ObjectId,
    ObjectLocation ExpectedSource,
    ObjectLocation Destination);

public enum ObjectTransferFailure
{
    None = 0,
    EmptyTransaction,
    DuplicateObject,
    StaleObject,
    SourceChanged,
    InvalidDestination,
    MissingParent,
    ParentCapability,
    ContainerCapacity,
    ParentCycle,
    EquipmentSlotOccupied,
    EquipmentRestriction,
    UnknownMap,
    OutOfMapBounds,
    Immovable,
    TerrainBlocked,
    ObjectBlocked,
}

public readonly record struct ObjectTransferResult(
    bool Succeeded,
    ObjectTransferFailure Failure,
    string Message,
    PlacementBlocker Blocker = default)
{
    public static ObjectTransferResult Success(int objectCount) =>
        new(
            true,
            ObjectTransferFailure.None,
            objectCount == 1
                ? "Transferred 1 object."
                : $"Transferred {objectCount} objects.");

    public static ObjectTransferResult Rejected(
        ObjectTransferFailure failure,
        string message,
        PlacementBlocker blocker = default) =>
        new(false, failure, message, blocker);
}

public sealed class ObjectTransferException : InvalidOperationException
{
    public ObjectTransferException(ObjectTransferResult result)
        : base(result.Message)
    {
        Result = result;
    }

    public ObjectTransferResult Result { get; }
}

/// <summary>
/// Validates a projected final object-location graph, then commits every
/// requested location together. No store mutation occurs on rejection.
/// </summary>
public sealed class ObjectTransferService
{
    private readonly ObjectStore _objects;

    public ObjectTransferService(ObjectStore objects)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    }

    public ObjectTransferResult Execute(
        params ObjectTransferRequest[] requests) =>
        Execute((IReadOnlyList<ObjectTransferRequest>)requests);

    public ObjectTransferResult Execute(
        IReadOnlyList<ObjectTransferRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var validation = Validate(requests);
        if (!validation.Succeeded)
        {
            return validation;
        }

        _objects.CommitTransfer(requests);
        return ObjectTransferResult.Success(requests.Count);
    }

    public void ExecuteOrThrow(
        params ObjectTransferRequest[] requests)
    {
        var result = Execute(requests);
        if (!result.Succeeded)
        {
            throw new ObjectTransferException(result);
        }
    }

    private ObjectTransferResult Validate(
        IReadOnlyList<ObjectTransferRequest> requests)
    {
        if (requests.Count == 0)
        {
            return Reject(
                ObjectTransferFailure.EmptyTransaction,
                "A transfer transaction requires at least one object.");
        }

        var objects = _objects.Enumerate();
        var byId = objects.ToDictionary(value => value.Id);
        var projected = objects.ToDictionary(
            value => value.Id,
            value => value.Location);
        var moved = new HashSet<ObjectId>();

        foreach (var request in requests)
        {
            if (!moved.Add(request.ObjectId))
            {
                return Reject(
                    ObjectTransferFailure.DuplicateObject,
                    $"Object {request.ObjectId} occurs more than once.");
            }

            if (!byId.TryGetValue(request.ObjectId, out var value))
            {
                return Reject(
                    ObjectTransferFailure.StaleObject,
                    $"Object {request.ObjectId} is absent, destroyed, or stale.");
            }

            if (value.Location != request.ExpectedSource)
            {
                return Reject(
                    ObjectTransferFailure.SourceChanged,
                    $"Object {request.ObjectId} is no longer at its expected " +
                    "source.");
            }

            try
            {
                request.Destination.Validate();
            }
            catch (ArgumentException exception)
            {
                return Reject(
                    ObjectTransferFailure.InvalidDestination,
                    exception.Message);
            }

            projected[request.ObjectId] = request.Destination;
        }

        foreach (var value in objects)
        {
            var location = projected[value.Id];
            if (location.Kind == LocationKind.OnMap)
            {
                try
                {
                    _ = WorldMap.VolumeFor(value with
                    {
                        Location = location,
                    });
                }
                catch (OverflowException)
                {
                    return Reject(
                        ObjectTransferFailure.InvalidDestination,
                        $"Destination for {value.Name} exceeds integer world " +
                        "bounds.");
                }
            }

            if (location.Kind is not
                (LocationKind.InContainer or LocationKind.Equipped))
            {
                continue;
            }

            if (!byId.TryGetValue(location.Parent, out var parent))
            {
                return Reject(
                    ObjectTransferFailure.MissingParent,
                    $"Destination parent {location.Parent} is absent or stale.");
            }

            if (location.Kind == LocationKind.InContainer &&
                !parent.HasFlag(ObjectFlags.Container))
            {
                return Reject(
                    ObjectTransferFailure.ParentCapability,
                    $"{parent.Name} is not a container.");
            }

            if (location.Kind == LocationKind.Equipped)
            {
                if (!parent.HasFlag(ObjectFlags.Actor))
                {
                    return Reject(
                        ObjectTransferFailure.ParentCapability,
                        $"{parent.Name} cannot carry equipment.");
                }

                if (!value.HasFlag(ObjectFlags.Item) ||
                    !value.EquipmentSlots.Accepts(location.Slot))
                {
                    return Reject(
                        ObjectTransferFailure.EquipmentRestriction,
                        $"{value.Name} cannot occupy equipment slot " +
                        $"{location.Slot}.");
                }
            }
        }

        foreach (var value in objects)
        {
            var cycle = ValidateParentChain(value.Id, projected);
            if (cycle is not null)
            {
                return cycle.Value;
            }
        }

        var containerCounts = new Dictionary<ObjectId, int>();
        var occupiedSlots = new HashSet<(ObjectId Actor, byte Slot)>();
        foreach (var value in objects)
        {
            var location = projected[value.Id];
            if (location.Kind == LocationKind.InContainer)
            {
                containerCounts[location.Parent] =
                    containerCounts.GetValueOrDefault(location.Parent) + 1;
            }
            else if (location.Kind == LocationKind.Equipped &&
                     !occupiedSlots.Add((location.Parent, location.Slot)))
            {
                return Reject(
                    ObjectTransferFailure.EquipmentSlotOccupied,
                    $"Equipment slot {location.Slot} on " +
                    $"{byId[location.Parent].Name} would be occupied twice.");
            }
        }

        foreach (var (containerId, count) in containerCounts)
        {
            var container = byId[containerId];
            if (count > container.ContainerCapacity)
            {
                return Reject(
                    ObjectTransferFailure.ContainerCapacity,
                    $"{container.Name} is full.");
            }
        }

        return ValidatePlacement(requests, byId);
    }

    /// <summary>
    /// Physical placement is a validation stage of the same transaction: every
    /// object whose destination is a map must fit there before anything commits.
    /// </summary>
    private ObjectTransferResult ValidatePlacement(
        IReadOnlyList<ObjectTransferRequest> requests,
        IReadOnlyDictionary<ObjectId, WorldObject> byId)
    {
        var moving = requests
            .Where(request =>
                request.Destination.Kind == LocationKind.OnMap)
            .ToDictionary(
                request => request.ObjectId,
                request => request.Destination);
        foreach (var request in requests.OrderBy(request => request.ObjectId))
        {
            if (request.Destination.Kind != LocationKind.OnMap)
            {
                continue;
            }

            var mapId = request.Destination.MapId;
            if (!_objects.Maps.TryGet(mapId, out var map))
            {
                return Reject(
                    ObjectTransferFailure.UnknownMap,
                    $"Map {mapId} is not registered in this world.");
            }

            var placement = map.ValidatePlacement(
                byId[request.ObjectId] with
                {
                    Location = request.Destination,
                },
                request.ExpectedSource,
                moving);
            if (!placement.Allowed)
            {
                return Reject(
                    ToTransferFailure(placement.Failure),
                    placement.Message,
                    placement.Blocker);
            }
        }

        return ObjectTransferResult.Success(requests.Count);
    }

    private static ObjectTransferFailure ToTransferFailure(
        PlacementFailure failure) =>
        failure switch
        {
            PlacementFailure.UnknownMap => ObjectTransferFailure.UnknownMap,
            PlacementFailure.OutOfMapBounds =>
                ObjectTransferFailure.OutOfMapBounds,
            PlacementFailure.Immovable => ObjectTransferFailure.Immovable,
            PlacementFailure.TerrainBlocked =>
                ObjectTransferFailure.TerrainBlocked,
            PlacementFailure.ObjectBlocked =>
                ObjectTransferFailure.ObjectBlocked,
            _ => throw new InvalidOperationException(
                $"Placement failure {failure} has no transfer failure."),
        };

    private static ObjectTransferResult? ValidateParentChain(
        ObjectId start,
        IReadOnlyDictionary<ObjectId, ObjectLocation> projected)
    {
        var visited = new HashSet<ObjectId>();
        var current = start;
        while (projected.TryGetValue(current, out var location) &&
               location.Kind is
                   LocationKind.InContainer or LocationKind.Equipped)
        {
            if (!visited.Add(current))
            {
                return Reject(
                    ObjectTransferFailure.ParentCycle,
                    $"The transfer would create an object-parent cycle at " +
                    $"{current}.");
            }

            current = location.Parent;
        }

        return null;
    }

    private static ObjectTransferResult Reject(
        ObjectTransferFailure failure,
        string message,
        PlacementBlocker blocker = default) =>
        ObjectTransferResult.Rejected(failure, message, blocker);
}

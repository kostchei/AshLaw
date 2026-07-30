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
}

public readonly record struct ObjectTransferResult(
    bool Succeeded,
    ObjectTransferFailure Failure,
    string Message)
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
        string message) =>
        new(false, failure, message);
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

        return ObjectTransferResult.Success(requests.Count);
    }

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
        string message) =>
        ObjectTransferResult.Rejected(failure, message);
}

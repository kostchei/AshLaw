using Ash.Core;
using System.Diagnostics;

namespace Ash.Sim;

public readonly struct ObjectId :
    IEquatable<ObjectId>,
    IComparable<ObjectId>
{
    public const int MaxIndex = 0x00FF_FFFF;

    private readonly uint _bits;

    private ObjectId(uint bits)
    {
        _bits = bits;
    }

    public static ObjectId None => default;

    public uint Value => _bits;

    public int Index => (int)(_bits & 0x00FF_FFFF);

    public byte Generation => (byte)(_bits >> 24);

    public bool IsNone => _bits == 0;

    internal static ObjectId FromParts(int index, byte generation)
    {
        if (index < 0 || index > MaxIndex)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (generation == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(generation),
                "Generation zero is reserved for ObjectId.None.");
        }

        return new ObjectId(((uint)generation << 24) | (uint)index);
    }

    public bool Equals(ObjectId other) => _bits == other._bits;

    public override bool Equals(object? obj) =>
        obj is ObjectId other && Equals(other);

    public override int GetHashCode() => _bits.GetHashCode();

    public int CompareTo(ObjectId other) => _bits.CompareTo(other._bits);

    public override string ToString() =>
        IsNone ? "ObjectId.None" : $"{Index}:{Generation}";

    public static bool operator ==(ObjectId left, ObjectId right) =>
        left.Equals(right);

    public static bool operator !=(ObjectId left, ObjectId right) =>
        !left.Equals(right);
}

public enum LocationKind : byte
{
    Invalid = 0,
    OnMap = 1,
    InContainer = 2,
    Equipped = 3,
    InTransfer = 4,
}

public readonly record struct ObjectLocation
{
    private readonly ushort _mapId;
    private readonly Vec3i _position;
    private readonly ObjectId _parent;
    private readonly byte _slot;
    private readonly uint _transferId;

    private ObjectLocation(
        LocationKind kind,
        ushort mapId = 0,
        Vec3i position = default,
        ObjectId parent = default,
        byte slot = 0,
        uint transferId = 0)
    {
        Kind = kind;
        _mapId = mapId;
        _position = position;
        _parent = parent;
        _slot = slot;
        _transferId = transferId;
    }

    public LocationKind Kind { get; }

    public ushort MapId => Kind == LocationKind.OnMap
        ? _mapId
        : throw WrongKind(nameof(MapId), LocationKind.OnMap);

    public Vec3i Position => Kind == LocationKind.OnMap
        ? _position
        : throw WrongKind(nameof(Position), LocationKind.OnMap);

    public ObjectId Parent => Kind is
        LocationKind.InContainer or LocationKind.Equipped
        ? _parent
        : throw WrongKind(
            nameof(Parent),
            LocationKind.InContainer,
            LocationKind.Equipped);

    public byte Slot => Kind == LocationKind.Equipped
        ? _slot
        : throw WrongKind(nameof(Slot), LocationKind.Equipped);

    public uint TransferId => Kind == LocationKind.InTransfer
        ? _transferId
        : throw WrongKind(nameof(TransferId), LocationKind.InTransfer);

    public static ObjectLocation OnMap(ushort mapId, Vec3i position) =>
        new(LocationKind.OnMap, mapId: mapId, position: position);

    public static ObjectLocation InContainer(ObjectId parent) =>
        new(LocationKind.InContainer, parent: parent);

    public static ObjectLocation Equipped(ObjectId actor, byte slot) =>
        new(LocationKind.Equipped, parent: actor, slot: slot);

    public static ObjectLocation Equipped(
        ObjectId actor,
        EquipmentSlot slot) =>
        Equipped(actor, (byte)slot);

    public static ObjectLocation InTransfer(uint transferId)
    {
        if (transferId == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(transferId),
                "Transfer id zero is reserved.");
        }

        return new ObjectLocation(
            LocationKind.InTransfer,
            transferId: transferId);
    }

    internal void Validate()
    {
        if (Kind == LocationKind.Invalid)
        {
            throw new ArgumentException(
                "Every live object requires one valid location.");
        }

        if (Kind is LocationKind.InContainer or LocationKind.Equipped &&
            _parent.IsNone)
        {
            throw new ArgumentException(
                $"{Kind} requires a non-empty parent handle.");
        }

        if (Kind == LocationKind.InTransfer && _transferId == 0)
        {
            throw new ArgumentException(
                "InTransfer requires a non-zero transfer id.");
        }
    }

    public override string ToString() =>
        Kind switch
        {
            LocationKind.OnMap =>
                $"OnMap(map={_mapId}, position={_position})",
            LocationKind.InContainer =>
                $"InContainer(parent={_parent})",
            LocationKind.Equipped =>
                $"Equipped(actor={_parent}, slot={_slot})",
            LocationKind.InTransfer =>
                $"InTransfer(transaction={_transferId})",
            _ => "Invalid",
        };

    private InvalidOperationException WrongKind(
        string member,
        params LocationKind[] expected) =>
        new(
            $"{member} does not exist for {Kind}; expected " +
            $"{string.Join(" or ", expected)}.");
}

[Flags]
public enum ObjectFlags
{
    None = 0,
    Solid = 1 << 0,
    Fixed = 1 << 1,
    Movable = 1 << 2,
    Usable = 1 << 3,
    Visible = 1 << 4,
    Damaging = 1 << 5,
    Container = 1 << 6,
    Actor = 1 << 7,
    Monster = 1 << 8,
    Corpse = 1 << 9,
    Item = 1 << 10,
    Trigger = 1 << 11,
}

[Flags]
public enum EquipmentSlotMask : ushort
{
    None = 0,
    MainHand = 1 << 0,
    OffHand = 1 << 1,
    Head = 1 << 2,
    Body = 1 << 3,
    Hands = 1 << 4,
    Feet = 1 << 5,
    Neck = 1 << 6,
    Ring = 1 << 7,
}

public enum EquipmentSlot : byte
{
    MainHand = 0,
    OffHand = 1,
    Head = 2,
    Body = 3,
    Hands = 4,
    Feet = 5,
    Neck = 6,
    Ring = 7,
}

public static class EquipmentSlots
{
    public static EquipmentSlotMask MaskFor(byte slot) =>
        slot < 16
            ? (EquipmentSlotMask)(1 << slot)
            : EquipmentSlotMask.None;

    public static bool Accepts(this EquipmentSlotMask mask, byte slot) =>
        (mask & MaskFor(slot)) != 0;
}

public readonly record struct ObjectFootprint(int Width, int Depth)
{
    public void Validate()
    {
        if (Width <= 0 || Depth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Width),
                "Object footprint dimensions must be positive.");
        }
    }
}

public sealed record ObjectSpawn
{
    public required string TypeId { get; init; }

    public required string Name { get; init; }

    public required string ShapeId { get; init; }

    public int FrameId { get; init; }

    public required ObjectLocation Location { get; init; }

    public ObjectFootprint Footprint { get; init; } = new(128, 128);

    public int Height { get; init; }

    public ObjectFlags Flags { get; init; } = ObjectFlags.Visible;

    public EquipmentSlotMask EquipmentSlots { get; init; }

    public int Quality { get; init; }

    public int Quantity { get; init; } = 1;

    public int Condition { get; init; } = 100;

    public int ContainerCapacity { get; init; }

    public int Health { get; init; }

    public int MaxHealth { get; init; }
}

public readonly record struct WorldObject(
    ObjectId Id,
    string TypeId,
    string Name,
    string ShapeId,
    int FrameId,
    ObjectLocation Location,
    ObjectFootprint Footprint,
    int Height,
    ObjectFlags Flags,
    EquipmentSlotMask EquipmentSlots,
    int Quality,
    int Quantity,
    int Condition,
    int Health,
    int MaxHealth,
    int ContainerCapacity,
    bool IsContainerOpen)
{
    public bool HasFlag(ObjectFlags flag) => (Flags & flag) != 0;

    public bool IsAlive => MaxHealth > 0 && Health > 0;
}

public sealed class InvalidObjectIdException : InvalidOperationException
{
    public InvalidObjectIdException(ObjectId id)
        : base($"Object handle {id} is absent, destroyed, or stale.")
    {
        ObjectId = id;
    }

    public ObjectId ObjectId { get; }
}

public enum ObjectStoreChangeKind
{
    Created,
    Updated,
    Transferred,
    Destroyed,
}

public sealed record ObjectStoreCommit(
    ObjectStoreChangeKind Kind,
    IReadOnlyList<ObjectId> ObjectIds);

/// <summary>
/// Authoritative object storage backed by parallel slot arrays and 32-bit
/// generational handles.
/// </summary>
public sealed class ObjectStore
{
    private readonly List<byte> _generations = [];
    private readonly List<bool> _alive = [];
    private readonly List<string?> _typeIds = [];
    private readonly List<string?> _names = [];
    private readonly List<string?> _shapeIds = [];
    private readonly List<int> _frameIds = [];
    private readonly List<ObjectLocation> _locations = [];
    private readonly List<ObjectFootprint> _footprints = [];
    private readonly List<int> _heights = [];
    private readonly List<ObjectFlags> _flags = [];
    private readonly List<EquipmentSlotMask> _equipmentSlots = [];
    private readonly List<int> _qualities = [];
    private readonly List<int> _quantities = [];
    private readonly List<int> _conditions = [];
    private readonly List<int> _health = [];
    private readonly List<int> _maxHealth = [];
    private readonly List<int> _containerCapacities = [];
    private readonly List<bool> _containerOpen = [];
    private readonly Stack<int> _freeSlots = [];

    public event Action<ObjectStoreCommit>? Committed;

    public int Count { get; private set; }

    public ObjectId Create(ObjectSpawn spawn)
    {
        ArgumentNullException.ThrowIfNull(spawn);
        ValidateSpawn(spawn);
        ValidateLocation(ObjectId.None, spawn.Location);

        var index = AllocateSlot();
        var id = ObjectId.FromParts(index, _generations[index]);
        _alive[index] = true;
        _typeIds[index] = spawn.TypeId;
        _names[index] = spawn.Name;
        _shapeIds[index] = spawn.ShapeId;
        _frameIds[index] = spawn.FrameId;
        _locations[index] = spawn.Location;
        _footprints[index] = spawn.Footprint;
        _heights[index] = spawn.Height;
        _flags[index] = spawn.Flags;
        _equipmentSlots[index] = spawn.EquipmentSlots;
        _qualities[index] = spawn.Quality;
        _quantities[index] = spawn.Quantity;
        _conditions[index] = spawn.Condition;
        _health[index] = spawn.Health;
        _maxHealth[index] = spawn.MaxHealth;
        _containerCapacities[index] = spawn.ContainerCapacity;
        _containerOpen[index] = false;
        Count++;
        AssertInvariants();
        Publish(ObjectStoreChangeKind.Created, id);
        return id;
    }

    public WorldObject Get(ObjectId id)
    {
        var index = ResolveSlot(id);
        return Snapshot(index);
    }

    public bool TryGet(ObjectId id, out WorldObject value)
    {
        if (!TryResolveSlot(id, out var index))
        {
            value = default;
            return false;
        }

        value = Snapshot(index);
        return true;
    }

    public IReadOnlyList<WorldObject> Enumerate()
    {
        var objects = new List<WorldObject>(Count);
        for (var index = 0; index < _alive.Count; index++)
        {
            if (_alive[index])
            {
                objects.Add(Snapshot(index));
            }
        }

        return objects;
    }

    public IReadOnlyList<ObjectId> GetContents(ObjectId container)
    {
        var containerIndex = ResolveSlot(container);
        RequireFlag(containerIndex, ObjectFlags.Container);
        var contents = new List<ObjectId>();
        for (var index = 0; index < _alive.Count; index++)
        {
            if (!_alive[index])
            {
                continue;
            }

            var location = _locations[index];
            if (location.Kind == LocationKind.InContainer &&
                location.Parent == container)
            {
                contents.Add(IdAt(index));
            }
        }

        return contents;
    }

    public void Move(ObjectId id, ObjectLocation destination)
    {
        var source = Get(id).Location;
        var result = new ObjectTransferService(this).Execute(
            new ObjectTransferRequest(id, source, destination));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Message);
        }
    }

    public int Damage(ObjectId id, int amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount));
        }

        var index = ResolveSlot(id);
        if (_maxHealth[index] <= 0)
        {
            throw new InvalidOperationException(
                $"Object {id} has no health component.");
        }

        _health[index] = Math.Max(0, _health[index] - amount);
        Publish(ObjectStoreChangeKind.Updated, id);
        return _health[index];
    }

    public void Transform(
        ObjectId id,
        string typeId,
        string name,
        string shapeId,
        ObjectFlags flags,
        int? height = null)
    {
        ValidateText(typeId, nameof(typeId));
        ValidateText(name, nameof(name));
        ValidateText(shapeId, nameof(shapeId));
        var index = ResolveSlot(id);
        if (flags.HasFlag(ObjectFlags.Container) !=
            (_containerCapacities[index] > 0))
        {
            throw new InvalidOperationException(
                $"Object {id} cannot change its container capability without " +
                "a matching capacity migration.");
        }

        if (!flags.HasFlag(ObjectFlags.Container) &&
            HasChildren(id, LocationKind.InContainer))
        {
            throw new InvalidOperationException(
                $"Object {id} cannot lose Container while it owns contents.");
        }

        if (!flags.HasFlag(ObjectFlags.Actor) &&
            HasChildren(id, LocationKind.Equipped))
        {
            throw new InvalidOperationException(
                $"Object {id} cannot lose Actor while equipment refers to it.");
        }

        if (height is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        if (height is not null &&
            _locations[index].Kind == LocationKind.OnMap)
        {
            _ = checked(_locations[index].Position.Z + height.Value);
        }

        _typeIds[index] = typeId;
        _names[index] = name;
        _shapeIds[index] = shapeId;
        _flags[index] = flags;
        if (height is not null)
        {
            _heights[index] = height.Value;
        }

        AssertInvariants();
        Publish(ObjectStoreChangeKind.Updated, id);
    }

    public void SetContainerOpen(ObjectId id, bool isOpen)
    {
        var index = ResolveSlot(id);
        RequireFlag(index, ObjectFlags.Container);
        _containerOpen[index] = isOpen;
        AssertInvariants();
        Publish(ObjectStoreChangeKind.Updated, id);
    }

    public void Destroy(ObjectId id)
    {
        var index = ResolveSlot(id);
        if (HasChildren(id))
        {
            throw new InvalidOperationException(
                $"Cannot destroy container/actor {id} while it owns objects.");
        }

        _alive[index] = false;
        _typeIds[index] = null;
        _names[index] = null;
        _shapeIds[index] = null;
        _locations[index] = default;
        _flags[index] = ObjectFlags.None;
        _equipmentSlots[index] = EquipmentSlotMask.None;
        _containerOpen[index] = false;
        _generations[index] = NextGeneration(_generations[index]);
        _freeSlots.Push(index);
        Count--;
        AssertInvariants();
        Publish(ObjectStoreChangeKind.Destroyed, id);
    }

    public void ValidateInvariants()
    {
        if (_alive.Count(value => value) != Count)
        {
            throw new InvalidOperationException(
                "Live-slot count does not match ObjectStore.Count.");
        }

        var equipmentSlots = new HashSet<(ObjectId Actor, byte Slot)>();
        for (var index = 0; index < _alive.Count; index++)
        {
            if (!_alive[index])
            {
                continue;
            }

            var id = IdAt(index);
            var location = _locations[index];
            location.Validate();
            _footprints[index].Validate();
            if (_quantities[index] <= 0 ||
                _health[index] < 0 ||
                _health[index] > _maxHealth[index])
            {
                throw new InvalidOperationException(
                    $"Object {id} has invalid quantity or health.");
            }

            var isContainer = _flags[index].HasFlag(ObjectFlags.Container);
            if (isContainer != (_containerCapacities[index] > 0))
            {
                throw new InvalidOperationException(
                    $"Object {id} container capability and capacity disagree.");
            }

            if (!isContainer && _containerOpen[index])
            {
                throw new InvalidOperationException(
                    $"Non-container object {id} is marked open.");
            }

            if (_equipmentSlots[index] != EquipmentSlotMask.None &&
                !_flags[index].HasFlag(ObjectFlags.Item))
            {
                throw new InvalidOperationException(
                    $"Non-item object {id} declares equipment slots.");
            }

            if (location.Kind is
                LocationKind.InContainer or LocationKind.Equipped)
            {
                var parent = location.Parent;
                var parentIndex = ResolveSlot(parent);
                var required = location.Kind == LocationKind.InContainer
                    ? ObjectFlags.Container
                    : ObjectFlags.Actor;
                RequireFlag(parentIndex, required);
                EnsureAcyclicParent(id, parent);

                if (location.Kind == LocationKind.Equipped &&
                    !equipmentSlots.Add((parent, location.Slot)))
                {
                    throw new InvalidOperationException(
                        $"Actor {parent} has duplicate equipment slot " +
                        $"{location.Slot}.");
                }

                if (location.Kind == LocationKind.Equipped &&
                    (!_flags[index].HasFlag(ObjectFlags.Item) ||
                     !_equipmentSlots[index].Accepts(location.Slot)))
                {
                    throw new InvalidOperationException(
                        $"Object {id} cannot occupy equipment slot " +
                        $"{location.Slot}.");
                }
            }

            if (isContainer &&
                GetContents(id).Count > _containerCapacities[index])
            {
                throw new InvalidOperationException(
                    $"Container {id} exceeds its capacity.");
            }
        }
    }

    private int AllocateSlot()
    {
        if (_freeSlots.TryPop(out var reused))
        {
            return reused;
        }

        var index = _alive.Count;
        if (index > ObjectId.MaxIndex)
        {
            throw new InvalidOperationException(
                "The 24-bit object-slot limit has been reached.");
        }

        _generations.Add(1);
        _alive.Add(false);
        _typeIds.Add(null);
        _names.Add(null);
        _shapeIds.Add(null);
        _frameIds.Add(0);
        _locations.Add(default);
        _footprints.Add(default);
        _heights.Add(0);
        _flags.Add(ObjectFlags.None);
        _equipmentSlots.Add(EquipmentSlotMask.None);
        _qualities.Add(0);
        _quantities.Add(0);
        _conditions.Add(0);
        _health.Add(0);
        _maxHealth.Add(0);
        _containerCapacities.Add(0);
        _containerOpen.Add(false);
        return index;
    }

    private void ValidateSpawn(ObjectSpawn spawn)
    {
        ValidateText(spawn.TypeId, nameof(spawn.TypeId));
        ValidateText(spawn.Name, nameof(spawn.Name));
        ValidateText(spawn.ShapeId, nameof(spawn.ShapeId));
        spawn.Location.Validate();
        spawn.Footprint.Validate();
        if (spawn.FrameId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawn.FrameId));
        }

        if (spawn.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawn.Height));
        }

        if (spawn.Location.Kind == LocationKind.OnMap)
        {
            var position = spawn.Location.Position;
            _ = checked(position.X - spawn.Footprint.Width);
            _ = checked(position.Y - spawn.Footprint.Depth);
            _ = checked(position.Z + spawn.Height);
        }

        if (spawn.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawn.Quantity));
        }

        if (spawn.Flags.HasFlag(ObjectFlags.Container) !=
            (spawn.ContainerCapacity > 0))
        {
            throw new ArgumentException(
                "Container flag and positive container capacity must agree.");
        }

        if (spawn.EquipmentSlots != EquipmentSlotMask.None &&
            !spawn.Flags.HasFlag(ObjectFlags.Item))
        {
            throw new ArgumentException(
                "Only item objects can declare accepted equipment slots.");
        }

        if (spawn.Location.Kind == LocationKind.Equipped &&
            (!spawn.Flags.HasFlag(ObjectFlags.Item) ||
             !spawn.EquipmentSlots.Accepts(spawn.Location.Slot)))
        {
            throw new ArgumentException(
                "An equipped spawn must accept its initial equipment slot.");
        }

        if (spawn.MaxHealth < 0 ||
            spawn.Health < 0 ||
            spawn.Health > spawn.MaxHealth)
        {
            throw new ArgumentException(
                "Health must be between zero and max health.");
        }
    }

    private void ValidateLocation(ObjectId moving, ObjectLocation location)
    {
        location.Validate();
        if (location.Kind == LocationKind.OnMap ||
            location.Kind == LocationKind.InTransfer)
        {
            return;
        }

        var parent = location.Parent;
        var parentIndex = ResolveSlot(parent);
        if (location.Kind == LocationKind.InContainer)
        {
            RequireFlag(parentIndex, ObjectFlags.Container);
            var alreadyThere =
                !moving.IsNone &&
                _locations[ResolveSlot(moving)].Kind ==
                LocationKind.InContainer &&
                _locations[ResolveSlot(moving)].Parent == parent;
            if (!alreadyThere &&
                GetContents(parent).Count >= _containerCapacities[parentIndex])
            {
                throw new InvalidOperationException(
                    $"Container {parent} is full.");
            }
        }
        else
        {
            RequireFlag(parentIndex, ObjectFlags.Actor);
            var occupied = Enumerate().Any(candidate =>
                candidate.Id != moving &&
                candidate.Location.Kind == LocationKind.Equipped &&
                candidate.Location.Parent == parent &&
                candidate.Location.Slot == location.Slot);
            if (occupied)
            {
                throw new InvalidOperationException(
                    $"Equipment slot {location.Slot} on {parent} is occupied.");
            }
        }

        if (!moving.IsNone)
        {
            EnsureAcyclicParent(moving, parent);
        }
    }

    private void EnsureAcyclicParent(ObjectId moving, ObjectId parent)
    {
        var visited = new HashSet<ObjectId>();
        var current = parent;
        while (!current.IsNone)
        {
            if (current == moving)
            {
                throw new InvalidOperationException(
                    $"Moving {moving} below {parent} would create a cycle.");
            }

            if (!visited.Add(current))
            {
                throw new InvalidOperationException(
                    $"Existing object-parent cycle found at {current}.");
            }

            var location = Get(current).Location;
            current = location.Kind is
                LocationKind.InContainer or LocationKind.Equipped
                ? location.Parent
                : ObjectId.None;
        }
    }

    private bool HasChildren(
        ObjectId parent,
        LocationKind? kind = null)
    {
        for (var index = 0; index < _alive.Count; index++)
        {
            if (!_alive[index])
            {
                continue;
            }

            var location = _locations[index];
            if ((location.Kind == LocationKind.InContainer ||
                 location.Kind == LocationKind.Equipped) &&
                (kind is null || location.Kind == kind) &&
                location.Parent == parent)
            {
                return true;
            }
        }

        return false;
    }

    private void RequireFlag(int index, ObjectFlags required)
    {
        if (!_flags[index].HasFlag(required))
        {
            throw new InvalidOperationException(
                $"Object {IdAt(index)} lacks required flag {required}.");
        }
    }

    private int ResolveSlot(ObjectId id)
    {
        if (!TryResolveSlot(id, out var index))
        {
            throw new InvalidObjectIdException(id);
        }

        return index;
    }

    private bool TryResolveSlot(ObjectId id, out int index)
    {
        index = id.Index;
        return !id.IsNone &&
               index >= 0 &&
               index < _alive.Count &&
               _alive[index] &&
               _generations[index] == id.Generation;
    }

    private ObjectId IdAt(int index) =>
        ObjectId.FromParts(index, _generations[index]);

    internal void CommitTransfer(
        IReadOnlyList<ObjectTransferRequest> requests)
    {
        foreach (var request in requests)
        {
            _locations[ResolveSlot(request.ObjectId)] = request.Destination;
        }

        AssertInvariants();
        Committed?.Invoke(
            new ObjectStoreCommit(
                ObjectStoreChangeKind.Transferred,
                requests.Select(request => request.ObjectId).ToArray()));
    }

    private WorldObject Snapshot(int index) =>
        new(
            IdAt(index),
            _typeIds[index]!,
            _names[index]!,
            _shapeIds[index]!,
            _frameIds[index],
            _locations[index],
            _footprints[index],
            _heights[index],
            _flags[index],
            _equipmentSlots[index],
            _qualities[index],
            _quantities[index],
            _conditions[index],
            _health[index],
            _maxHealth[index],
            _containerCapacities[index],
            _containerOpen[index]);

    [Conditional("DEBUG")]
    private void AssertInvariants() => ValidateInvariants();

    private static byte NextGeneration(byte generation) =>
        generation == byte.MaxValue ? (byte)1 : (byte)(generation + 1);

    private static void ValidateText(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Value must not be empty.",
                parameter);
        }
    }

    private void Publish(ObjectStoreChangeKind kind, ObjectId id) =>
        Committed?.Invoke(new ObjectStoreCommit(kind, [id]));
}

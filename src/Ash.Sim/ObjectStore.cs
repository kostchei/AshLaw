using Ash.Core;
using Ash.Rules;
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

    /// <summary>
    /// A non-solid top face objects still rest on, such as a bridge deck.
    /// Every <see cref="Solid"/> volume already supports what lands on it, so
    /// nothing that stops a fall is ever missing a support surface.
    /// </summary>
    ProvidesSupport = 1 << 12,

    AffectedByGravity = 1 << 13,

    /// <summary>
    /// Identical objects of this kind combine into one handle carrying a
    /// quantity, rather than sitting beside each other as separate objects.
    /// </summary>
    Stackable = 1 << 14,

    /// <summary>
    /// A weapon light enough to be driven by dexterity — a rapier, a dagger, a
    /// whip — when that serves the wielder better than strength.
    /// </summary>
    Finesse = 1 << 15,

    /// <summary>
    /// An equippable attack source. Its immutable rules profile is selected by
    /// type id; this flag prevents food, tools and other hand items from being
    /// mistaken for weapons.
    /// </summary>
    Weapon = 1 << 16,

    /// <summary>An item destroyed by a combat effect and no longer usable.</summary>
    Broken = 1 << 17,
}

/// <summary>
/// Where a thing is worn. These are places on the body, not carrying capacity:
/// worn gear costs no gear slots, so what you wear is what you get for free.
/// </summary>
public enum EquipmentSlot : byte
{
    RightHand = 0,
    LeftHand = 1,

    /// <summary>Helmet.</summary>
    Head = 2,

    /// <summary>The matched outfit: none, robes, leather, chain or plate.</summary>
    Body = 3,

    Gloves = 4,

    /// <summary>Boots, which are a pair and one slot.</summary>
    Boots = 5,

    Cloak = 6,
    Necklace = 7,
    Belt = 8,

    /// <summary>Hangs from the belt and holds a blade.</summary>
    Scabbard = 9,

    /// <summary>Hangs from the belt and holds small goods.</summary>
    BeltPouch = 10,

    RingLeft = 11,
    RingRight = 12,
}

[Flags]
public enum EquipmentSlotMask : ushort
{
    None = 0,
    RightHand = 1 << EquipmentSlot.RightHand,
    LeftHand = 1 << EquipmentSlot.LeftHand,
    Head = 1 << EquipmentSlot.Head,
    Body = 1 << EquipmentSlot.Body,
    Gloves = 1 << EquipmentSlot.Gloves,
    Boots = 1 << EquipmentSlot.Boots,
    Cloak = 1 << EquipmentSlot.Cloak,
    Necklace = 1 << EquipmentSlot.Necklace,
    Belt = 1 << EquipmentSlot.Belt,
    Scabbard = 1 << EquipmentSlot.Scabbard,
    BeltPouch = 1 << EquipmentSlot.BeltPouch,
    RingLeft = 1 << EquipmentSlot.RingLeft,
    RingRight = 1 << EquipmentSlot.RingRight,

    /// <summary>Either hand.</summary>
    EitherHand = RightHand | LeftHand,

    /// <summary>Either ring finger.</summary>
    EitherRing = RingLeft | RingRight,
}

public static class EquipmentSlots
{
    /// <summary>Every place on the body, in the order a paper doll reads.</summary>
    public static readonly IReadOnlyList<EquipmentSlot> All =
    [
        EquipmentSlot.Head,
        EquipmentSlot.Necklace,
        EquipmentSlot.Cloak,
        EquipmentSlot.Body,
        EquipmentSlot.Belt,
        EquipmentSlot.Scabbard,
        EquipmentSlot.BeltPouch,
        EquipmentSlot.Gloves,
        EquipmentSlot.RightHand,
        EquipmentSlot.LeftHand,
        EquipmentSlot.RingRight,
        EquipmentSlot.RingLeft,
        EquipmentSlot.Boots,
    ];

    public static EquipmentSlotMask MaskFor(byte slot) =>
        slot < 16
            ? (EquipmentSlotMask)(1 << slot)
            : EquipmentSlotMask.None;

    public static bool Accepts(this EquipmentSlotMask mask, byte slot) =>
        (mask & MaskFor(slot)) != 0;

    public static bool IsBody(byte slot) =>
        All.Contains((EquipmentSlot)slot);
}

/// <summary>
/// Carrying capacity, measured in gear slots rather than weight.
/// </summary>
public static class GearSlots
{
    /// <summary>
    /// Even the weakest character carries this much:
    /// <c>capacity = max(Strength, MinimumCapacity)</c>.
    /// </summary>
    public const int MinimumCapacity = 10;

    /// <summary>
    /// The largest carried inventory that can exist, and so the number of rows
    /// the UI must have room for: strength alone reaches 20, and class
    /// abilities can add up to five more slots on top.
    /// </summary>
    public const int PanelCapacity = 25;

    /// <summary>One slot: most gear, a weapon, a whole stack, a torch.</summary>
    public const int StandardCost = 1;

    /// <summary>A hundred coins of any mix fill one slot.</summary>
    public const int CoinsPerSlot = 100;

    /// <summary>Ten gems of any kind fill one slot.</summary>
    public const int GemsPerSlot = 10;

    /// <summary>Twenty arrows or bolts fill one slot.</summary>
    public const int AmmunitionPerSlot = 20;

    /// <summary>Ten iron spikes fill one slot.</summary>
    public const int SpikesPerSlot = 10;

    /// <summary>Three rations fill one slot.</summary>
    public const int RationsPerSlot = 3;

    /// <summary>Coins pool across denominations; gems pool across kinds.</summary>
    public const string CoinGroup = "coins";

    public const string GemGroup = "gems";

    /// <summary>
    /// One entry of a carried inventory as the player sees it: what it is, how
    /// many, and how many gear slots it fills. Pooled goods appear once with
    /// their combined count, because that is how they take up room.
    /// </summary>
    public readonly record struct GearSlotEntry(
        ObjectId ObjectId,
        string Label,
        int Quantity,
        int Cells,
        bool IsPooled);

    /// <summary>
    /// Lays a carried inventory out slot by slot, so a panel can draw one cell
    /// per gear slot without knowing the rules. The entries always add up to
    /// <see cref="UsedBy"/>.
    /// </summary>
    public static IReadOnlyList<GearSlotEntry> LayOut(
        IEnumerable<WorldObject> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var entries = new List<GearSlotEntry>();
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in contents)
        {
            if (value.QuantityPerSlot <= 0)
            {
                entries.Add(
                    new GearSlotEntry(
                        value.Id,
                        value.Name,
                        value.Quantity,
                        Math.Max(1, value.SlotCost),
                        false));
                continue;
            }

            var group = value.SlotGroup.Length > 0
                ? value.SlotGroup
                : value.TypeId;
            if (groups.TryGetValue(group, out var at))
            {
                var running = entries[at];
                entries[at] = running with
                {
                    Quantity = checked(running.Quantity + value.Quantity),
                    Cells = (checked(running.Quantity + value.Quantity) +
                        value.QuantityPerSlot - 1) / value.QuantityPerSlot,
                    Label = value.SlotGroup.Length > 0
                        ? char.ToUpperInvariant(value.SlotGroup[0]) +
                          value.SlotGroup[1..]
                        : running.Label,
                    IsPooled = true,
                };
                continue;
            }

            groups.Add(group, entries.Count);
            entries.Add(
                new GearSlotEntry(
                    value.Id,
                    value.Name,
                    value.Quantity,
                    (value.Quantity + value.QuantityPerSlot - 1) /
                        value.QuantityPerSlot,
                    false));
        }

        return entries;
    }

    /// <summary>
    /// The gear slots a set of carried objects takes up. Objects that measure
    /// themselves by count — coins, gems, ammunition — pool with everything in
    /// the same group, so fifty gold and fifty silver are still one slot of
    /// coins. Everything else costs its own slots.
    /// </summary>
    public static int UsedBy(IEnumerable<WorldObject> contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var used = 0;
        var pooled = new Dictionary<string, (int Quantity, int PerSlot)>(
            StringComparer.Ordinal);
        foreach (var value in contents)
        {
            if (value.QuantityPerSlot <= 0)
            {
                used = checked(used + value.SlotCost);
                continue;
            }

            var group = value.SlotGroup.Length > 0
                ? value.SlotGroup
                : value.TypeId;
            if (pooled.TryGetValue(group, out var running))
            {
                if (running.PerSlot != value.QuantityPerSlot)
                {
                    throw new InvalidOperationException(
                        $"Group '{group}' is authored with both " +
                        $"{running.PerSlot} and {value.QuantityPerSlot} per " +
                        "slot.");
                }

                pooled[group] = (
                    checked(running.Quantity + value.Quantity),
                    running.PerSlot);
                continue;
            }

            pooled[group] = (value.Quantity, value.QuantityPerSlot);
        }

        foreach (var (quantity, perSlot) in pooled.Values)
        {
            used = checked(used + ((quantity + perSlot - 1) / perSlot));
        }

        return used;
    }

    /// <summary>Two slots: plate, two-handed weapons, awkward loot.</summary>
    public const int BulkyCost = 2;

    /// <summary>
    /// <c>max(Strength, 10)</c>, plus whatever a class ability grants, and
    /// never more than the panel can show.
    /// </summary>
    public static int CapacityFor(int strength, int bonus = 0) =>
        Math.Min(
            checked(Math.Max(strength, MinimumCapacity) + bonus),
            PanelCapacity);
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

    /// <summary>
    /// How far this object may rise or drop in one step and stay attached to a
    /// support. Only meaningful for objects that move themselves.
    /// </summary>
    public int StepHeight { get; init; }

    public ObjectFlags Flags { get; init; } = ObjectFlags.Visible;

    public EquipmentSlotMask EquipmentSlots { get; init; }

    public int Quality { get; init; }

    public int Quantity { get; init; } = 1;

    /// <summary>
    /// How many of this kind one handle may carry. One means the object never
    /// stacks, which is the default and what every non-stackable object uses.
    /// </summary>
    public int MaxQuantity { get; init; } = 1;

    public int Condition { get; init; } = 100;

    /// <summary>
    /// Gear slots this occupies while carried. A stack costs its slots once,
    /// however many it holds; worn gear costs nothing at all.
    /// </summary>
    public int SlotCost { get; init; } = GearSlots.StandardCost;

    /// <summary>
    /// Slots this container holds. Actors derive theirs from
    /// <see cref="Strength"/> instead and must leave this at zero.
    /// </summary>
    public int SlotCapacity { get; init; }

    /// <summary>
    /// How many of this fit in one gear slot, for goods measured by count:
    /// 100 coins, 10 gems, 20 arrows, 10 spikes, 3 rations. Zero means the
    /// object costs <see cref="SlotCost"/> whatever its quantity.
    /// </summary>
    public int QuantityPerSlot { get; init; }

    /// <summary>
    /// Goods that share a slot with other kinds — coins of any denomination,
    /// gems of any sort. Empty means it pools only with its own type.
    /// </summary>
    public string SlotGroup { get; init; } = string.Empty;

    /// <summary>The actor stat carrying capacity is derived from.</summary>
    public int Strength { get; init; }

    public int Dexterity { get; init; }

    public int Constitution { get; init; }

    public int Intelligence { get; init; }

    public int Wisdom { get; init; }

    public int Charisma { get; init; }

    /// <summary>
    /// The class whose attack progression this actor advances on. Only
    /// meaningful at <see cref="Level"/> one or above.
    /// </summary>
    public CharacterClass Class { get; init; } = CharacterClass.Fighter;

    /// <summary>
    /// Character level. Zero means the object has no class progression at all,
    /// which is every prop, item and unlevelled creature.
    /// </summary>
    public int Level { get; init; }

    /// <summary>The armour category this object counts as when worn.</summary>
    public ArmorType ArmorType { get; init; } = ArmorType.None;

    /// <summary>
    /// What wearing or carrying this adds to its owner's Defense Modifier:
    /// armour's own protection, or a shield's.
    /// </summary>
    public int DefenseBonus { get; init; }

    /// <summary>
    /// Extra gear slots from a class ability or similar, added on top of the
    /// strength-derived capacity.
    /// </summary>
    public int GearSlotBonus { get; init; }

    /// <summary>Current concussion hits: the outer layer of punishment.</summary>
    public int Health { get; init; }

    public int MaxHealth { get; init; }

    /// <summary>
    /// Current wounds: the layer under concussion hits. A spawn starts whole,
    /// so the death-save counters and impairment are not authored — they are
    /// what happens to a body, not what it was made with.
    /// </summary>
    public int Wounds { get; init; }

    public int MaxWounds { get; init; }

    /// <summary>
    /// The object this spawn would become, so capacity and placement can be
    /// validated before anything is created.
    /// </summary>
    public WorldObject AsProbe(ObjectLocation location) =>
        new(
            ObjectId.None,
            TypeId,
            Name,
            ShapeId,
            FrameId,
            location,
            Footprint,
            Height,
            StepHeight,
            MotionState.Resting,
            0,
            SupportRef.None,
            Flags,
            EquipmentSlots,
            Quality,
            Quantity,
            MaxQuantity,
            Condition,
            Health,
            MaxHealth,
            Wounds,
            MaxWounds,
            0,
            0,
            AbilityMask.None,
            VitalityState.Standing,
            SlotCost,
            SlotCapacity,
            QuantityPerSlot,
            SlotGroup,
            Strength,
            Dexterity,
            Constitution,
            Intelligence,
            Wisdom,
            Charisma,
            Class,
            Level,
            ArmorType,
            DefenseBonus,
            GearSlotBonus,
            false);
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
    int StepHeight,
    MotionState Motion,
    int VerticalVelocity,
    SupportRef Support,
    ObjectFlags Flags,
    EquipmentSlotMask EquipmentSlots,
    int Quality,
    int Quantity,
    int MaxQuantity,
    int Condition,
    int Health,
    int MaxHealth,
    int Wounds,
    int MaxWounds,
    int DeathSaveSuccesses,
    int DeathSaveFailures,
    AbilityMask Impairments,
    VitalityState Vitality,
    int SlotCost,
    int SlotCapacity,
    int QuantityPerSlot,
    string SlotGroup,
    int Strength,
    int Dexterity,
    int Constitution,
    int Intelligence,
    int Wisdom,
    int Charisma,
    CharacterClass Class,
    int Level,
    ArmorType ArmorType,
    int DefenseBonus,
    int GearSlotBonus,
    bool IsContainerOpen)
{
    public bool HasFlag(ObjectFlags flag) => (Flags & flag) != 0;

    /// <summary>
    /// Whether the body is still in the world as something other than a corpse.
    /// Running out of concussion hits is no longer the same as dying: a body
    /// with a wound layer keeps going into it, and one on the death clock is
    /// alive until the clock says otherwise.
    /// </summary>
    public bool IsAlive => MaxHealth > 0 && Vitality != VitalityState.Dead;

    /// <summary>Everything about how hurt this body is, as one value.</summary>
    public InjuryState Injury =>
        new(
            Health,
            MaxHealth,
            Wounds,
            MaxWounds,
            DeathSaveSuccesses,
            DeathSaveFailures,
            Impairments,
            Vitality);

    /// <summary>
    /// Whether this object's top face can hold another object. Solid volumes
    /// stop a fall, so they must also end it; <see cref="ObjectFlags.ProvidesSupport"/>
    /// adds the same surface to volumes that are not solid.
    /// </summary>
    public bool CanSupport =>
        HasFlag(ObjectFlags.Solid) || HasFlag(ObjectFlags.ProvidesSupport);

    /// <summary>
    /// How many gear slots this container offers. An actor's capacity comes
    /// from its strength — <c>max(Strength, 10)</c> — and everything else uses
    /// the slots it was authored with.
    /// </summary>
    /// <summary>
    /// The six scores as the rules see them. Anything unrolled reads as the
    /// minimum, which is what a prop or an unlevelled creature has.
    /// </summary>
    public AbilityScores Abilities =>
        new(
            Math.Max(Strength, AbilityScores.MinimumScore),
            Math.Max(Dexterity, AbilityScores.MinimumScore),
            Math.Max(Constitution, AbilityScores.MinimumScore),
            Math.Max(Intelligence, AbilityScores.MinimumScore),
            Math.Max(Wisdom, AbilityScores.MinimumScore),
            Math.Max(Charisma, AbilityScores.MinimumScore));

    public int CarryCapacity =>
        !HasFlag(ObjectFlags.Container)
            ? 0
            : Strength > 0
                ? GearSlots.CapacityFor(Strength, GearSlotBonus)
                : SlotCapacity;
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

/// <summary>A stack's new size, committed with the rest of its transaction.</summary>
public readonly record struct ObjectQuantityUpdate(
    ObjectId ObjectId,
    int Quantity);

public sealed record ObjectStoreCommit(
    ObjectStoreChangeKind Kind,
    IReadOnlyList<ObjectId> ObjectIds);

/// <summary>
/// The state this combat slice can change in one authoritative commit. Later
/// phases extend this value with conditions, displacement and equipment
/// effects; keeping injury as a value now prevents callers from publishing a
/// half-resolved body.
/// </summary>
public sealed record CombatTransform(
    ObjectId ObjectId,
    string TypeId,
    string Name,
    string ShapeId,
    ObjectFlags Flags,
    int? Height = null);

public sealed record CombatMutation(
    ObjectId TargetId,
    InjuryState? Injury = null,
    IReadOnlyList<ObjectTransferRequest>? Transfers = null,
    IReadOnlyList<ObjectId>? BreakItems = null,
    CombatTransform? Transform = null,
    IReadOnlyList<ActorConditionMutation>? Conditions = null,
    IReadOnlyList<ObjectPhysicsUpdate>? Physics = null,
    IReadOnlyList<ActorConditionConsumption>? ConditionConsumptions = null,
    IReadOnlyList<ObjectId>? ClearConditions = null);

/// <summary>
/// Post-mutation checkpoints exposed solely as a deterministic fault-injection
/// seam. A failure at any checkpoint must restore every earlier stage.
/// </summary>
public enum CombatMutationStage : byte
{
    Injury,
    Transfers,
    Physics,
    Items,
    Transform,
    Conditions,
}

/// <summary>
/// One object slot exactly as the store holds it. A dead slot still carries its
/// generation, which is what makes a handle from before its destruction stale
/// after a save and load.
/// </summary>
public readonly record struct ObjectSlotSnapshot(
    bool IsAlive,
    byte Generation,
    WorldObject? Value);

public sealed record ObjectStoreSnapshot(
    IReadOnlyList<ObjectSlotSnapshot> Slots,
    IReadOnlyList<int> FreeSlots);

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
    private readonly List<int> _stepHeights = [];
    private readonly List<MotionState> _motionStates = [];
    private readonly List<int> _verticalVelocities = [];
    private readonly List<SupportRef> _supports = [];
    private readonly List<ObjectFlags> _flags = [];
    private readonly List<EquipmentSlotMask> _equipmentSlots = [];
    private readonly List<int> _qualities = [];
    private readonly List<int> _quantities = [];
    private readonly List<int> _maxQuantities = [];
    private readonly List<int> _conditions = [];
    private readonly List<int> _health = [];
    private readonly List<int> _maxHealth = [];
    private readonly List<int> _wounds = [];
    private readonly List<int> _maxWounds = [];
    private readonly List<int> _deathSaveSuccesses = [];
    private readonly List<int> _deathSaveFailures = [];
    private readonly List<AbilityMask> _impairments = [];
    private readonly List<VitalityState> _vitality = [];
    private readonly List<int> _slotCosts = [];
    private readonly List<int> _slotCapacities = [];
    private readonly List<int> _quantitiesPerSlot = [];
    private readonly List<string> _slotGroups = [];
    private readonly List<int> _strengths = [];
    private readonly List<int> _dexterities = [];
    private readonly List<int> _constitutions = [];
    private readonly List<int> _intelligences = [];
    private readonly List<int> _wisdoms = [];
    private readonly List<int> _charismas = [];
    private readonly List<CharacterClass> _classes = [];
    private readonly List<int> _levels = [];
    private readonly List<ArmorType> _armorTypes = [];
    private readonly List<int> _defenseBonuses = [];
    private readonly List<int> _gearSlotBonuses = [];
    private readonly List<bool> _containerOpen = [];
    private readonly Stack<int> _freeSlots = [];

    public event Action<ObjectStoreCommit>? Committed;

    /// <summary>
    /// The maps of this world. <see cref="ObjectStore"/> still owns identity and
    /// components; the set exists so one transaction can reach the authoritative
    /// terrain and spatial index of a destination map.
    /// </summary>
    public WorldMapSet Maps { get; } = new();

    public int Count { get; private set; }

    /// <summary>
    /// Whether a commit is in flight, including the change notifications it
    /// publishes. The save boundary refuses to snapshot a world in this state.
    /// </summary>
    public bool IsCommitting { get; private set; }

    /// <summary>
    /// Every slot of this store, live and dead, in slot order, plus the
    /// free-slot stack in pop order. Dead slots and their generations are part
    /// of the authoritative state: they are what keeps a stale handle stale and
    /// future allocation deterministic.
    /// </summary>
    public ObjectStoreSnapshot Capture()
    {
        var slots = new List<ObjectSlotSnapshot>(_alive.Count);
        for (var index = 0; index < _alive.Count; index++)
        {
            slots.Add(
                new ObjectSlotSnapshot(
                    _alive[index],
                    _generations[index],
                    _alive[index] ? Snapshot(index) : null));
        }

        // Stack<int> enumerates top first, which is the order the slots will be
        // reused in. The reader pushes them back in reverse.
        return new ObjectStoreSnapshot(slots, _freeSlots.ToArray());
    }

    /// <summary>
    /// Rebuilds a store from <paramref name="snapshot"/> with identical handles.
    /// Nothing is remapped: slot indices, generations, the live/dead pattern and
    /// the free-slot order are restored exactly, then the invariants are checked.
    /// </summary>
    public static ObjectStore Restore(ObjectStoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var store = new ObjectStore();
        var dead = new HashSet<int>();
        for (var index = 0; index < snapshot.Slots.Count; index++)
        {
            var slot = snapshot.Slots[index];
            store.AllocateSlot();
            store._generations[index] = slot.Generation;
            if (!slot.IsAlive)
            {
                dead.Add(index);
                if (slot.Value is not null)
                {
                    throw new ArgumentException(
                        $"Slot {index} is dead but carries an object.");
                }

                continue;
            }

            if (slot.Value is not { } value)
            {
                throw new ArgumentException(
                    $"Slot {index} is live but carries no object.");
            }

            if (value.Id.Index != index ||
                value.Id.Generation != slot.Generation)
            {
                throw new ArgumentException(
                    $"Object {value.Id} does not belong in slot {index} " +
                    $"generation {slot.Generation}.");
            }

            store.WriteSlot(index, value);
            store.Count++;
        }

        var free = snapshot.FreeSlots;
        if (free.Count != dead.Count ||
            free.Distinct().Count() != free.Count ||
            free.Any(index => !dead.Contains(index)))
        {
            throw new ArgumentException(
                "The free-slot stack must hold every dead slot exactly once.");
        }

        for (var index = free.Count - 1; index >= 0; index--)
        {
            store._freeSlots.Push(free[index]);
        }

        store.ValidateInvariants();
        return store;
    }

    public ObjectId Create(ObjectSpawn spawn)
    {
        var id = CreateWithoutPublishing(spawn);
        AssertInvariants();
        Publish(ObjectStoreChangeKind.Created, id);
        return id;
    }

    private ObjectId CreateWithoutPublishing(ObjectSpawn spawn)
    {
        ArgumentNullException.ThrowIfNull(spawn);
        ValidateSpawn(spawn);
        ValidateLocation(
            ObjectId.None,
            spawn.Location,
            spawn.AsProbe(spawn.Location));

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
        _stepHeights[index] = spawn.StepHeight;

        // Anything gravity moves enters the world falling: the first physics
        // tick is what decides where it comes to rest, so no spawn can invent a
        // support that the support graph never validated.
        var falls = spawn.Flags.HasFlag(ObjectFlags.AffectedByGravity) &&
            spawn.Location.Kind == LocationKind.OnMap;
        _motionStates[index] = falls
            ? MotionState.Falling
            : MotionState.Resting;
        _verticalVelocities[index] = 0;
        _supports[index] = SupportRef.None;
        _flags[index] = spawn.Flags;
        _equipmentSlots[index] = spawn.EquipmentSlots;
        _qualities[index] = spawn.Quality;
        _quantities[index] = spawn.Quantity;
        _maxQuantities[index] = spawn.MaxQuantity;
        _conditions[index] = spawn.Condition;
        _health[index] = spawn.Health;
        _maxHealth[index] = spawn.MaxHealth;
        _wounds[index] = spawn.Wounds;
        _maxWounds[index] = spawn.MaxWounds;
        _deathSaveSuccesses[index] = 0;
        _deathSaveFailures[index] = 0;
        _impairments[index] = AbilityMask.None;
        _vitality[index] = VitalityState.Standing;
        _slotCosts[index] = spawn.SlotCost;
        _slotCapacities[index] = spawn.SlotCapacity;
        _quantitiesPerSlot[index] = spawn.QuantityPerSlot;
        _slotGroups[index] = spawn.SlotGroup;
        _strengths[index] = spawn.Strength;
        _dexterities[index] = spawn.Dexterity;
        _constitutions[index] = spawn.Constitution;
        _intelligences[index] = spawn.Intelligence;
        _wisdoms[index] = spawn.Wisdom;
        _charismas[index] = spawn.Charisma;
        _classes[index] = spawn.Class;
        _levels[index] = spawn.Level;
        _armorTypes[index] = spawn.ArmorType;
        _defenseBonuses[index] = spawn.DefenseBonus;
        _gearSlotBonuses[index] = spawn.GearSlotBonus;
        _containerOpen[index] = false;
        Count++;
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

    public void BreakItem(ObjectId id)
    {
        var index = ResolveSlot(id);
        if (!_flags[index].HasFlag(ObjectFlags.Item))
        {
            throw new InvalidOperationException($"Object {id} is not an item.");
        }

        _flags[index] |= ObjectFlags.Broken;
        _conditions[index] = 0;
        Publish(ObjectStoreChangeKind.Updated, id);
    }

    /// <summary>
    /// Writes a body's whole injury state back in one go.
    /// </summary>
    /// <remarks>
    /// The store owns the numbers; <see cref="Ash.Rules.Injury"/> owns what
    /// they mean. Every transition — damage, a death save, healing, a day's
    /// rest — is resolved there as a pure function and lands here as one
    /// assignment, so concussion hits, wounds, the death clock and impairment
    /// can never be written half-updated relative to each other.
    /// </remarks>
    public void SetInjury(ObjectId id, InjuryState injury)
    {
        CommitCombatMutation(new CombatMutation(id, injury));
    }

    /// <summary>
    /// Commits a character's six permanent ability scores as one update.
    /// Talent resolution uses this before deriving the next level's body.
    /// </summary>
    public void SetActorAbilities(ObjectId id, AbilityScores abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        var index = ResolveSlot(id);
        RequireFlag(index, ObjectFlags.Actor);
        _strengths[index] = abilities.Strength;
        _dexterities[index] = abilities.Dexterity;
        _constitutions[index] = abilities.Constitution;
        _intelligences[index] = abilities.Intelligence;
        _wisdoms[index] = abilities.Wisdom;
        _charismas[index] = abilities.Charisma;
        AssertInvariants();
        Publish(ObjectStoreChangeKind.Updated, id);
    }

    /// <summary>
    /// Advances one living actor to the next level and expands both vitality
    /// layers atomically. Existing damage is retained: the new hit die and new
    /// wound capacity are added as filled points, but nothing else is healed.
    /// </summary>
    public void AdvanceActorLevel(
        ObjectId id,
        int newLevel,
        int addedConcussion,
        int newMaximumWounds)
    {
        var index = ResolveSlot(id);
        RequireFlag(index, ObjectFlags.Actor);
        if (_vitality[index] == VitalityState.Dead)
        {
            throw new InvalidOperationException("A dead actor cannot advance.");
        }
        if (newLevel != _levels[index] + 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newLevel),
                newLevel,
                "Advancement must add exactly one level.");
        }
        if (addedConcussion < 1 || newMaximumWounds < _maxWounds[index])
        {
            throw new ArgumentOutOfRangeException(
                nameof(addedConcussion),
                "Advancement cannot reduce either vitality layer.");
        }

        _levels[index] = newLevel;
        _maxHealth[index] = checked(_maxHealth[index] + addedConcussion);
        _health[index] = checked(_health[index] + addedConcussion);
        var addedWounds = newMaximumWounds - _maxWounds[index];
        _maxWounds[index] = newMaximumWounds;
        _wounds[index] = checked(_wounds[index] + addedWounds);
        AssertInvariants();
        Publish(ObjectStoreChangeKind.Updated, id);
    }

    /// <summary>
    /// Validates then applies the complete current combat mutation under one
    /// commit boundary and publishes only after the store is coherent.
    /// </summary>
    public void CommitCombatMutation(
        CombatMutation mutation,
        ActorConditionService? actorConditions = null,
        Action<CombatMutationStage>? afterStage = null)
    {
        var index = ResolveSlot(mutation.TargetId);
        var injury = mutation.Injury;
        if (injury is not null && _maxHealth[index] <= 0)
        {
            throw new InvalidOperationException(
                $"Object {mutation.TargetId} has no health component.");
        }

        if (injury is not null &&
            (injury.Value.MaximumConcussion != _maxHealth[index] ||
             injury.Value.MaximumWounds != _maxWounds[index]))
        {
            throw new InvalidOperationException(
                $"Object {mutation.TargetId} was given an injury state built for a different " +
                "body. Its maximums change when the character does, not when " +
                "it is hurt.");
        }

        if (injury is not null &&
            (injury.Value.Concussion < 0 ||
             injury.Value.Concussion > injury.Value.MaximumConcussion ||
             injury.Value.Wounds < 0 ||
             injury.Value.Wounds > injury.Value.MaximumWounds ||
             injury.Value.DeathSaveSuccesses < 0 ||
             injury.Value.DeathSaveFailures < 0))
        {
            throw new InvalidOperationException(
                $"Object {mutation.TargetId} was given an out-of-range injury state.");
        }

        var transfers = mutation.Transfers ?? [];
        if (transfers.Count > 0)
        {
            var validation = new ObjectTransferService(this).ValidateForCommit(transfers);
            if (!validation.Succeeded)
            {
                throw new ObjectTransferException(validation);
            }
        }

        var breakItems = mutation.BreakItems ?? [];
        foreach (var itemId in breakItems.Distinct())
        {
            var itemIndex = ResolveSlot(itemId);
            RequireFlag(itemIndex, ObjectFlags.Item);
        }

        var physics = mutation.Physics ?? [];
        foreach (var update in physics)
        {
            update.Validate();
            _ = ResolveSlot(update.ObjectId);
        }

        if (mutation.Transform is { } transform)
        {
            ValidateCombatTransform(transform, transfers);
        }

        foreach (var condition in mutation.Conditions ?? [])
        {
            var actorIndex = ResolveSlot(condition.ActorId);
            RequireFlag(actorIndex, ObjectFlags.Actor);
            if (!ActorConditionService.CanStore(condition.Effect.Kind))
            {
                throw new InvalidOperationException(
                    $"{condition.Effect.Kind} is not a persistent actor condition.");
            }

            _ = ConditionTiming.ExpiryTick(condition.AppliedTick, condition.Effect);
        }

        var conditionConsumptions = mutation.ConditionConsumptions ?? [];
        var clearConditions = mutation.ClearConditions ?? [];
        if ((mutation.Conditions?.Count ?? 0) > 0 ||
            conditionConsumptions.Count > 0 || clearConditions.Count > 0)
        {
            ArgumentNullException.ThrowIfNull(actorConditions);
        }

        foreach (var consumption in conditionConsumptions)
        {
            var actorIndex = ResolveSlot(consumption.ActorId);
            RequireFlag(actorIndex, ObjectFlags.Actor);
            if (!ActorConditionService.CanStore(consumption.Kind))
            {
                throw new InvalidOperationException(
                    $"{consumption.Kind} is not a persistent actor condition.");
            }

            var scheduled = (mutation.Conditions ?? []).Any(condition =>
                condition.ActorId == consumption.ActorId &&
                condition.Effect.Kind == consumption.Kind &&
                (!consumption.SourceId.HasValue ||
                 condition.SourceId == consumption.SourceId.Value));
            if (!scheduled && !actorConditions!.Has(
                    consumption.ActorId,
                    consumption.Kind,
                    consumption.SourceId))
            {
                throw new InvalidOperationException(
                    $"The {consumption.Kind} condition to consume is no longer present.");
            }
        }

        foreach (var actorId in clearConditions.Distinct())
        {
            var actorIndex = ResolveSlot(actorId);
            RequireFlag(actorIndex, ObjectFlags.Actor);
        }

        var touchedIds = new[] { mutation.TargetId }
            .Concat(transfers.Select(transfer => transfer.ObjectId))
            .Concat(breakItems)
            .Concat(physics.Select(update => update.ObjectId))
            .Concat(mutation.Transform is null ? [] : [mutation.Transform.ObjectId])
            .Concat((mutation.Conditions ?? []).Select(change => change.ActorId))
            .Concat(conditionConsumptions.Select(change => change.ActorId))
            .Concat(clearConditions)
            .Distinct()
            .Order()
            .ToArray();
        var objectRollback = touchedIds.ToDictionary(
            id => id.Index,
            id => Get(id));
        var conditionRollback = actorConditions?.Capture();

        IsCommitting = true;
        try
        {
            try
            {
                if (injury is not null)
                {
                    _health[index] = injury.Value.Concussion;
                    _wounds[index] = injury.Value.Wounds;
                    _deathSaveSuccesses[index] = injury.Value.DeathSaveSuccesses;
                    _deathSaveFailures[index] = injury.Value.DeathSaveFailures;
                    _impairments[index] = injury.Value.Impairments;
                    _vitality[index] = injury.Value.State;
                    afterStage?.Invoke(CombatMutationStage.Injury);
                }

                ApplyTransfersWithoutPublishing(transfers);
                if (transfers.Count > 0)
                {
                    afterStage?.Invoke(CombatMutationStage.Transfers);
                }
                foreach (var update in physics)
                {
                    var physicsIndex = ResolveSlot(update.ObjectId);
                    _motionStates[physicsIndex] = update.Motion;
                    _verticalVelocities[physicsIndex] = update.VerticalVelocity;
                    _supports[physicsIndex] = update.Support;
                }
                if (physics.Count > 0)
                {
                    afterStage?.Invoke(CombatMutationStage.Physics);
                }
                foreach (var itemId in breakItems.Distinct())
                {
                    var itemIndex = ResolveSlot(itemId);
                    _flags[itemIndex] |= ObjectFlags.Broken;
                    _conditions[itemIndex] = 0;
                }
                if (breakItems.Count > 0)
                {
                    afterStage?.Invoke(CombatMutationStage.Items);
                }

                if (mutation.Transform is { } appliedTransform)
                {
                    ApplyCombatTransform(appliedTransform);
                    afterStage?.Invoke(CombatMutationStage.Transform);
                }

                foreach (var change in mutation.Conditions ?? [])
                {
                    actorConditions!.Apply(
                        change.ActorId,
                        change.SourceId,
                        change.Effect,
                        change.AppliedTick);
                }
                foreach (var consumption in conditionConsumptions)
                {
                    if (!actorConditions!.Consume(
                            consumption.ActorId,
                            consumption.Kind,
                            consumption.SourceId))
                    {
                        throw new InvalidOperationException(
                            $"The {consumption.Kind} condition could not be consumed.");
                    }
                }
                foreach (var actorId in clearConditions.Distinct())
                {
                    actorConditions!.RemoveAll(actorId);
                }
                if ((mutation.Conditions?.Count ?? 0) > 0 ||
                    conditionConsumptions.Count > 0 || clearConditions.Count > 0)
                {
                    afterStage?.Invoke(CombatMutationStage.Conditions);
                }

                AssertInvariants();
            }
            catch
            {
                foreach (var (slot, value) in objectRollback)
                {
                    WriteSlot(slot, value);
                }

                if (actorConditions is not null && conditionRollback is not null)
                {
                    actorConditions.Restore(conditionRollback);
                }

                AssertInvariants();
                throw;
            }

            // Publication begins only after rollback-capable mutation has
            // succeeded. Observers therefore see exactly one final state.
            Committed?.Invoke(new ObjectStoreCommit(
                ObjectStoreChangeKind.Updated,
                touchedIds));
        }
        finally
        {
            IsCommitting = false;
        }
    }

    private void ValidateCombatTransform(
        CombatTransform transform,
        IReadOnlyList<ObjectTransferRequest> transfers)
    {
        ValidateText(transform.TypeId, nameof(transform.TypeId));
        ValidateText(transform.Name, nameof(transform.Name));
        ValidateText(transform.ShapeId, nameof(transform.ShapeId));
        var transformIndex = ResolveSlot(transform.ObjectId);
        if (transform.Flags.HasFlag(ObjectFlags.Container) !=
            (CarryCapacityAt(transformIndex) > 0))
        {
            throw new InvalidOperationException(
                $"Object {transform.ObjectId} cannot change its container capability.");
        }

        var projected = transfers.ToDictionary(
            transfer => transfer.ObjectId,
            transfer => transfer.Destination);
        bool HasProjectedChildren(LocationKind kind) => Enumerate().Any(value =>
            (projected.TryGetValue(value.Id, out var location) ? location : value.Location) is var current &&
            current.Kind == kind && current.Parent == transform.ObjectId);
        if (!transform.Flags.HasFlag(ObjectFlags.Container) &&
            HasProjectedChildren(LocationKind.InContainer))
        {
            throw new InvalidOperationException("The transform would orphan contained objects.");
        }

        if (!transform.Flags.HasFlag(ObjectFlags.Actor) &&
            HasProjectedChildren(LocationKind.Equipped))
        {
            throw new InvalidOperationException("The transform would orphan equipped objects.");
        }

        if (transform.Height is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(transform.Height));
        }
    }

    private void ApplyCombatTransform(CombatTransform transform)
    {
        var index = ResolveSlot(transform.ObjectId);
        _typeIds[index] = transform.TypeId;
        _names[index] = transform.Name;
        _shapeIds[index] = transform.ShapeId;
        _flags[index] = transform.Flags;
        if (transform.Height is { } height)
        {
            _heights[index] = height;
        }
    }

    private void ApplyTransfersWithoutPublishing(
        IReadOnlyList<ObjectTransferRequest> requests)
    {
        foreach (var request in requests)
        {
            var movedIndex = ResolveSlot(request.ObjectId);
            _locations[movedIndex] = request.Destination;
            if (request.Destination.Kind != LocationKind.OnMap)
            {
                _motionStates[movedIndex] = MotionState.Resting;
                _verticalVelocities[movedIndex] = 0;
                _supports[movedIndex] = SupportRef.None;
            }
            else if (_flags[movedIndex].HasFlag(ObjectFlags.AffectedByGravity))
            {
                _motionStates[movedIndex] = MotionState.Falling;
                _verticalVelocities[movedIndex] = 0;
                _supports[movedIndex] = SupportRef.None;
            }
        }
    }

    /// <summary>How hurt a body is right now.</summary>
    public InjuryState InjuryOf(ObjectId id)
    {
        var index = ResolveSlot(id);
        if (_maxHealth[index] <= 0)
        {
            throw new InvalidOperationException(
                $"Object {id} has no health component.");
        }

        return new InjuryState(
            _health[index],
            _maxHealth[index],
            _wounds[index],
            _maxWounds[index],
            _deathSaveSuccesses[index],
            _deathSaveFailures[index],
            _impairments[index],
            _vitality[index]);
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
            (CarryCapacityAt(index) > 0))
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
        DestroyWithoutPublishing(id);
        AssertInvariants();
        Publish(ObjectStoreChangeKind.Destroyed, id);
    }

    private void DestroyWithoutPublishing(ObjectId id)
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
        _motionStates[index] = MotionState.Resting;
        _verticalVelocities[index] = 0;
        _supports[index] = SupportRef.None;
        _flags[index] = ObjectFlags.None;
        _equipmentSlots[index] = EquipmentSlotMask.None;
        _containerOpen[index] = false;
        _generations[index] = NextGeneration(_generations[index]);
        _freeSlots.Push(index);
        Count--;
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
                _maxQuantities[index] < 1 ||
                _quantities[index] > _maxQuantities[index] ||
                _health[index] < 0 ||
                _health[index] > _maxHealth[index])
            {
                throw new InvalidOperationException(
                    $"Object {id} has invalid quantity or health.");
            }

            if (_wounds[index] < 0 ||
                _wounds[index] > _maxWounds[index] ||
                _deathSaveSuccesses[index] < 0 ||
                _deathSaveFailures[index] < 0)
            {
                throw new InvalidOperationException(
                    $"Object {id} has an invalid wound or death-clock state.");
            }

            // A body is on the death clock exactly when it has run out of both
            // layers and has a wound layer to have run out of. Anything else is
            // two numbers disagreeing about whether someone is dying.
            if (_vitality[index] == VitalityState.Standing &&
                _maxHealth[index] > 0 &&
                _health[index] == 0 &&
                _wounds[index] == 0)
            {
                throw new InvalidOperationException(
                    $"Object {id} is standing with nothing left to stand on.");
            }

            if (_vitality[index] is VitalityState.Dying or VitalityState.Stable &&
                (_wounds[index] > 0 || _maxWounds[index] < 1))
            {
                throw new InvalidOperationException(
                    $"Object {id} is {_vitality[index]} without being out of " +
                    "the wounds that put it there.");
            }

            if (_maxQuantities[index] > 1 &&
                !_flags[index].HasFlag(ObjectFlags.Stackable))
            {
                throw new InvalidOperationException(
                    $"Non-stackable object {id} has a stack limit above one.");
            }

            if (_verticalVelocities[index] < 0)
            {
                throw new InvalidOperationException(
                    $"Object {id} has a negative fall speed.");
            }

            if (_motionStates[index] == MotionState.Falling &&
                (location.Kind != LocationKind.OnMap ||
                 !_supports[index].IsNone))
            {
                throw new InvalidOperationException(
                    $"Falling object {id} must be on a map with no support.");
            }

            if (location.Kind != LocationKind.OnMap &&
                (!_supports[index].IsNone ||
                 _verticalVelocities[index] != 0))
            {
                throw new InvalidOperationException(
                    $"Object {id} carries map physics state off the map.");
            }

            if (_flags[index].HasFlag(ObjectFlags.Fixed) &&
                _motionStates[index] == MotionState.Falling)
            {
                throw new InvalidOperationException(
                    $"Fixed object {id} cannot be falling.");
            }

            var isContainer = _flags[index].HasFlag(ObjectFlags.Container);
            if (isContainer
                ? CarryCapacityAt(index) <= 0
                : _slotCapacities[index] != 0)
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

            if (isContainer && UsedSlots(id) > CarryCapacityAt(index))
            {
                throw new InvalidOperationException(
                    $"Container {id} holds {UsedSlots(id)} slots of a " +
                    $"{CarryCapacityAt(index)}-slot capacity.");
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
        _stepHeights.Add(0);
        _motionStates.Add(MotionState.Resting);
        _verticalVelocities.Add(0);
        _supports.Add(SupportRef.None);
        _flags.Add(ObjectFlags.None);
        _equipmentSlots.Add(EquipmentSlotMask.None);
        _qualities.Add(0);
        _quantities.Add(0);
        _maxQuantities.Add(1);
        _conditions.Add(0);
        _health.Add(0);
        _maxHealth.Add(0);
        _wounds.Add(0);
        _maxWounds.Add(0);
        _deathSaveSuccesses.Add(0);
        _deathSaveFailures.Add(0);
        _impairments.Add(AbilityMask.None);
        _vitality.Add(VitalityState.Standing);
        _slotCosts.Add(GearSlots.StandardCost);
        _slotCapacities.Add(0);
        _quantitiesPerSlot.Add(0);
        _slotGroups.Add(string.Empty);
        _strengths.Add(0);
        _dexterities.Add(0);
        _constitutions.Add(0);
        _intelligences.Add(0);
        _wisdoms.Add(0);
        _charismas.Add(0);
        _classes.Add(CharacterClass.Fighter);
        _levels.Add(0);
        _armorTypes.Add(ArmorType.None);
        _defenseBonuses.Add(0);
        _gearSlotBonuses.Add(0);
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

        if (spawn.StepHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawn.StepHeight));
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

        if (spawn.SlotCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawn.SlotCost));
        }

        if (spawn.SlotCapacity < 0 ||
            spawn.Strength < 0 ||
            spawn.GearSlotBonus < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawn.SlotCapacity));
        }

        if (spawn.Level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawn.Level));
        }

        if (spawn.Level > 0 && !spawn.Flags.HasFlag(ObjectFlags.Actor))
        {
            throw new ArgumentException(
                "Only an actor advances on a class progression.");
        }

        if (spawn.DefenseBonus < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(spawn.DefenseBonus));
        }

        if (!spawn.Flags.HasFlag(ObjectFlags.Actor) && spawn.GearSlotBonus != 0)
        {
            throw new ArgumentException(
                "Only actors carry gear slots, so only actors can have a bonus.");
        }

        if (spawn.Flags.HasFlag(ObjectFlags.Actor) && spawn.SlotCapacity != 0)
        {
            throw new ArgumentException(
                "An actor's carrying capacity comes from its strength, so its " +
                "slot capacity must be left at zero.");
        }

        if (spawn.MaxQuantity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(spawn.MaxQuantity));
        }

        if (spawn.Quantity > spawn.MaxQuantity)
        {
            throw new ArgumentException(
                $"A stack of {spawn.Quantity} exceeds the maximum of " +
                $"{spawn.MaxQuantity} for {spawn.TypeId}.");
        }

        if (spawn.MaxQuantity > 1 &&
            !spawn.Flags.HasFlag(ObjectFlags.Stackable))
        {
            throw new ArgumentException(
                "Only stackable objects can carry more than one.");
        }

        if (spawn.Flags.HasFlag(ObjectFlags.Container) &&
            spawn.SlotCapacity <= 0 &&
            spawn.Strength <= 0)
        {
            throw new ArgumentException(
                "A container needs slots: authored capacity, or the strength " +
                "an actor carries with.");
        }

        if (!spawn.Flags.HasFlag(ObjectFlags.Container) &&
            spawn.SlotCapacity != 0)
        {
            throw new ArgumentException(
                "Only a container has slot capacity.");
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

        if (spawn.MaxWounds < 0 ||
            spawn.Wounds < 0 ||
            spawn.Wounds > spawn.MaxWounds)
        {
            throw new ArgumentException(
                "Wounds must be between zero and max wounds.");
        }

        if (spawn.MaxWounds > 0 && spawn.MaxHealth < 1)
        {
            throw new ArgumentException(
                "A wound layer sits under concussion hits, so a body with " +
                "wounds must have concussion hits to lose first.");
        }
    }

    private void ValidateLocation(
        ObjectId moving,
        ObjectLocation location,
        WorldObject incoming)
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
            var projected = GetContents(parent)
                .Select(Get)
                .Append(incoming)
                .ToArray();
            if (!alreadyThere &&
                GearSlots.UsedBy(projected) > CarryCapacityAt(parentIndex))
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

    /// <summary>
    /// The gear slots a container's contents take up. Worn gear is not counted:
    /// it is on the body, not in the pack.
    /// </summary>
    private int UsedSlots(ObjectId container) =>
        GearSlots.UsedBy(GetContents(container).Select(Get));

    /// <summary>
    /// A body carries what its strength allows, alive or dead — a corpse is
    /// still a container of that size. Everything else uses its authored slots.
    /// </summary>
    private int CarryCapacityAt(int index) =>
        !_flags[index].HasFlag(ObjectFlags.Container)
            ? 0
            : _strengths[index] > 0
                ? GearSlots.CapacityFor(
                    _strengths[index],
                    _gearSlotBonuses[index])
                : _slotCapacities[index];

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

    /// <summary>
    /// Applies a stack change as one commit: quantity changes, at most one
    /// object created, and any emptied stacks destroyed. Splitting and merging
    /// must not be two commits, or a crash between them would create or destroy
    /// goods out of nothing.
    /// </summary>
    internal ObjectId CommitStackChange(
        IReadOnlyList<ObjectQuantityUpdate> quantities,
        IReadOnlyList<ObjectId> destroy,
        ObjectSpawn? create)
    {
        IsCommitting = true;
        try
        {
            var touched = new List<ObjectId>();
            foreach (var update in quantities)
            {
                var index = ResolveSlot(update.ObjectId);
                if (update.Quantity < 1 ||
                    update.Quantity > _maxQuantities[index])
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(quantities),
                        $"Object {update.ObjectId} cannot hold " +
                        $"{update.Quantity} of a maximum " +
                        $"{_maxQuantities[index]}.");
                }

                _quantities[index] = update.Quantity;
                touched.Add(update.ObjectId);
            }

            var created = ObjectId.None;
            if (create is not null)
            {
                created = CreateWithoutPublishing(create);
                touched.Add(created);
            }

            foreach (var id in destroy)
            {
                DestroyWithoutPublishing(id);
                touched.Add(id);
            }

            AssertInvariants();
            Committed?.Invoke(
                new ObjectStoreCommit(
                    ObjectStoreChangeKind.Updated,
                    touched.Distinct().Order().ToArray()));
            return created;
        }
        finally
        {
            IsCommitting = false;
        }
    }

    internal void CommitTransfer(
        IReadOnlyList<ObjectTransferRequest> requests,
        IReadOnlyList<ObjectPhysicsUpdate> physics)
    {
        IsCommitting = true;
        try
        {
            CommitLocked(requests, physics);
        }
        finally
        {
            IsCommitting = false;
        }
    }

    private void CommitLocked(
        IReadOnlyList<ObjectTransferRequest> requests,
        IReadOnlyList<ObjectPhysicsUpdate> physics)
    {
        foreach (var request in requests)
        {
            var index = ResolveSlot(request.ObjectId);
            _locations[index] = request.Destination;

            // A location change without an explicit physics update still has to
            // leave the support graph honest: an object that left the map keeps
            // no support, and one that arrived on a map without a resolved
            // support falls until the next tick finds one.
            if (request.Destination.Kind != LocationKind.OnMap)
            {
                _motionStates[index] = MotionState.Resting;
                _verticalVelocities[index] = 0;
                _supports[index] = SupportRef.None;
            }
            else if (_flags[index].HasFlag(ObjectFlags.AffectedByGravity))
            {
                _motionStates[index] = MotionState.Falling;
                _verticalVelocities[index] = 0;
                _supports[index] = SupportRef.None;
            }
        }

        foreach (var update in physics)
        {
            var index = ResolveSlot(update.ObjectId);
            _motionStates[index] = update.Motion;
            _verticalVelocities[index] = update.VerticalVelocity;
            _supports[index] = update.Support;
        }

        AssertInvariants();
        Committed?.Invoke(
            new ObjectStoreCommit(
                ObjectStoreChangeKind.Transferred,
                requests.Select(request => request.ObjectId)
                    .Concat(physics.Select(update => update.ObjectId))
                    .Distinct()
                    .Order()
                    .ToArray()));
    }

    private void WriteSlot(int index, WorldObject value)
    {
        _alive[index] = true;
        _typeIds[index] = value.TypeId;
        _names[index] = value.Name;
        _shapeIds[index] = value.ShapeId;
        _frameIds[index] = value.FrameId;
        _locations[index] = value.Location;
        _footprints[index] = value.Footprint;
        _heights[index] = value.Height;
        _stepHeights[index] = value.StepHeight;
        _motionStates[index] = value.Motion;
        _verticalVelocities[index] = value.VerticalVelocity;
        _supports[index] = value.Support;
        _flags[index] = value.Flags;
        _equipmentSlots[index] = value.EquipmentSlots;
        _qualities[index] = value.Quality;
        _quantities[index] = value.Quantity;
        _maxQuantities[index] = value.MaxQuantity;
        _conditions[index] = value.Condition;
        _health[index] = value.Health;
        _maxHealth[index] = value.MaxHealth;
        _wounds[index] = value.Wounds;
        _maxWounds[index] = value.MaxWounds;
        _deathSaveSuccesses[index] = value.DeathSaveSuccesses;
        _deathSaveFailures[index] = value.DeathSaveFailures;
        _impairments[index] = value.Impairments;
        _vitality[index] = value.Vitality;
        _slotCosts[index] = value.SlotCost;
        _slotCapacities[index] = value.SlotCapacity;
        _quantitiesPerSlot[index] = value.QuantityPerSlot;
        _slotGroups[index] = value.SlotGroup;
        _strengths[index] = value.Strength;
        _dexterities[index] = value.Dexterity;
        _constitutions[index] = value.Constitution;
        _intelligences[index] = value.Intelligence;
        _wisdoms[index] = value.Wisdom;
        _charismas[index] = value.Charisma;
        _classes[index] = value.Class;
        _levels[index] = value.Level;
        _armorTypes[index] = value.ArmorType;
        _defenseBonuses[index] = value.DefenseBonus;
        _gearSlotBonuses[index] = value.GearSlotBonus;
        _containerOpen[index] = value.IsContainerOpen;
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
            _stepHeights[index],
            _motionStates[index],
            _verticalVelocities[index],
            _supports[index],
            _flags[index],
            _equipmentSlots[index],
            _qualities[index],
            _quantities[index],
            _maxQuantities[index],
            _conditions[index],
            _health[index],
            _maxHealth[index],
            _wounds[index],
            _maxWounds[index],
            _deathSaveSuccesses[index],
            _deathSaveFailures[index],
            _impairments[index],
            _vitality[index],
            _slotCosts[index],
            _slotCapacities[index],
            _quantitiesPerSlot[index],
            _slotGroups[index],
            _strengths[index],
            _dexterities[index],
            _constitutions[index],
            _intelligences[index],
            _wisdoms[index],
            _charismas[index],
            _classes[index],
            _levels[index],
            _armorTypes[index],
            _defenseBonuses[index],
            _gearSlotBonuses[index],
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

    private void Publish(ObjectStoreChangeKind kind, ObjectId id)
    {
        IsCommitting = true;
        try
        {
            Committed?.Invoke(new ObjectStoreCommit(kind, [id]));
        }
        finally
        {
            IsCommitting = false;
        }
    }
}

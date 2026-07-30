using Ash.Core;

namespace Ash.Sim;

public readonly record struct GridPosition(int X, int Y)
{
    public GridPosition Offset(int deltaX, int deltaY) =>
        new(X + deltaX, Y + deltaY);

    public int ManhattanDistance(GridPosition other) =>
        Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}

public sealed record SliceActionResult(bool Succeeded, string Message);

/// <summary>
/// The playable interaction slice as commands and read models over the
/// authoritative <see cref="ObjectStore"/>.
/// </summary>
public sealed class PlayableSliceWorld
{
    public const ushort DemoMapId = 0;
    public const int MapWidth = 41;
    public const int MapHeight = 29;
    public const int WorldUnitsPerTile = WorldMap.WorldUnitsPerTile;
    public const int PlayerAttackDamage = 2;

    /// <summary>Eight world units is one Ultima VIII vertical level.</summary>
    public const int UnitsPerLevel = 8;

    /// <summary>The Avatar climbs one level per step without a jump.</summary>
    public const int StepHeightUnits = UnitsPerLevel;

    public const int PlatformZ = 4 * UnitsPerLevel;
    public const int PitFloorZ = -8 * UnitsPerLevel;
    public const int StairsXMin = 26;
    public const int StairsXMax = 29;
    public const int PlatformXMin = 30;
    public const int PlatformXMax = 34;
    public const int TerraceYMin = 3;
    public const int TerraceYMax = 8;
    public const int PitXMin = 20;
    public const int PitXMax = 23;
    public const int PitYMin = 23;
    public const int PitYMax = 26;
    public const int BridgeY = 24;

    private const int TableHeight = 40;
    private const int CrateHeight = 32;
    private const int BridgeDeckHeight = 4;

    private ObjectId _activeChestId = ObjectId.None;

    private PlayableSliceWorld(ObjectStore objects, ObjectId playerId)
    {
        Objects = objects;
        Transfers = new ObjectTransferService(objects);
        Movement = new MovementSolver(objects);
        PlayerId = playerId;
        Map = new WorldMap(
            objects,
            DemoMapId,
            MapWidth,
            MapHeight);
        BuildTerrain(Map);
        Physics = new PhysicsSystem(objects);
        Settle();
        LastMessage = "Explore. Open a chest or fight a monster.";
    }

    public ObjectStore Objects { get; }

    public ObjectTransferService Transfers { get; }

    public MovementSolver Movement { get; }

    public PhysicsSystem Physics { get; }

    public WorldMap Map { get; }

    public ObjectId PlayerId { get; }

    public WorldObject Player => Objects.Get(PlayerId);

    public GridPosition PlayerPosition => GridPositionOf(Player);

    public int PlayerHealth => Player.Health;

    public int PlayerMaxHealth => Player.MaxHealth;

    public bool BackpackOpen { get; private set; }

    public string LastMessage { get; private set; }

    public IReadOnlyList<WorldObject> Chests =>
        Map.QueryAll(ObjectFlags.Container)
            .Where(candidate =>
                candidate.Id != PlayerId &&
                !candidate.HasFlag(ObjectFlags.Monster))
            .ToArray();

    public IReadOnlyList<WorldObject> Monsters =>
        Map.QueryAll(ObjectFlags.Monster)
            .Where(candidate =>
                candidate.IsAlive)
            .ToArray();

    public WorldObject? ActiveChest =>
        _activeChestId.IsNone
            ? null
            : Objects.TryGet(_activeChestId, out var active)
                ? active
                : null;

    public IReadOnlyList<WorldObject> BackpackItems =>
        ContentsOf(PlayerId);

    public IReadOnlyList<WorldObject> EquippedItems =>
        Objects.Enumerate()
            .Where(candidate =>
                candidate.Location.Kind == LocationKind.Equipped &&
                candidate.Location.Parent == PlayerId)
            .OrderBy(candidate => candidate.Location.Slot)
            .ToArray();

    public IReadOnlyList<WorldObject> GroundItems =>
        Map.QueryAll(ObjectFlags.Item);

    public int BackpackCapacity => Player.ContainerCapacity;

    public static PlayableSliceWorld CreateDemo()
    {
        var objects = new ObjectStore();
        var player = objects.Create(new ObjectSpawn
        {
            TypeId = "actor.avatar",
            Name = "Avatar",
            ShapeId = "avatar.knight",
            Location = MapLocation(new GridPosition(4, 14)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            StepHeight = StepHeightUnits,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.AffectedByGravity |
                ObjectFlags.Visible,
            ContainerCapacity = 12,
            Health = 12,
            MaxHealth = 12,
        });
        SpawnItem(
            objects,
            player,
            "item.rusty-sword",
            "Rusty Sword",
            "loot.shortsword",
            EquipmentSlotMask.MainHand);
        SpawnItem(objects, player, "item.apple", "Apple");

        SpawnChest(
            objects,
            "container.store-room",
            "Store-room Chest",
            new GridPosition(8, 13),
            ["Health Tonic", "Iron Key", "12 Gold"]);
        SpawnChest(
            objects,
            "container.old-coffer",
            "Old Coffer",
            new GridPosition(21, 8),
            ["Moonstone", "Bandage"]);
        SpawnChest(
            objects,
            "container.pilgrims-cache",
            "Pilgrim's Cache",
            new GridPosition(31, 21),
            ["Silver Mirror", "Incense", "18 Gold"]);
        SpawnChest(
            objects,
            "container.vault-box",
            "Vault Box",
            new GridPosition(37, 12),
            ["Star Sapphire", "Antidote"]);

        SpawnMonster(
            objects,
            "monster.cave-rat",
            "Cave Rat",
            "monster.goblin",
            new GridPosition(13, 14),
            maxHealth: 4,
            loot: ["Rat Tail"]);
        SpawnMonster(
            objects,
            "monster.goblin-scout",
            "Goblin Scout",
            "monster.goblin",
            new GridPosition(24, 18),
            maxHealth: 6,
            loot: ["Copper Ring", "Throwing Knife"]);
        SpawnMonster(
            objects,
            "monster.many-eyed-tyrant",
            "Many-Eyed Tyrant",
            "monster.many-eyed",
            new GridPosition(35, 8),
            maxHealth: 8,
            loot: ["Glass Eye", "Nullstone Shard"]);
        SpawnMonster(
            objects,
            "monster.goblin-guard",
            "Goblin Guard",
            "monster.goblin",
            new GridPosition(30, 22),
            maxHealth: 8,
            loot: ["Iron Buckle", "Guard Token"]);

        SpawnPhysicsArea(objects);
        return new PlayableSliceWorld(objects, player);
    }

    /// <summary>
    /// The physical acceptance area: a table holding loot, a stack of crates, a
    /// bridge deck over a lower floor, and a trestle whose support the player
    /// can remove with <see cref="RemoveTrestleSupport"/>.
    /// </summary>
    private static void SpawnPhysicsArea(ObjectStore objects)
    {
        SpawnProp(
            objects,
            "prop.oak-table",
            "Oak Table",
            new GridPosition(10, 17),
            height: TableHeight,
            gravity: false);
        SpawnWorldItem(
            objects,
            "Brass Candlestick",
            new GridPosition(10, 17),
            TableHeight);

        SpawnProp(
            objects,
            "prop.crate-lower",
            "Crate",
            new GridPosition(12, 19),
            height: CrateHeight);
        SpawnProp(
            objects,
            "prop.crate-upper",
            "Stacked Crate",
            new GridPosition(12, 19),
            height: CrateHeight,
            baseZ: CrateHeight);
        SpawnProp(
            objects,
            "prop.crate-side",
            "Side Crate",
            new GridPosition(13, 19),
            height: CrateHeight);

        SpawnProp(
            objects,
            "prop.trestle",
            "Prop Trestle",
            new GridPosition(9, 20),
            height: TableHeight);
        SpawnWorldItem(
            objects,
            "Clay Urn",
            new GridPosition(9, 20),
            TableHeight);

        for (var x = PitXMin; x <= PitXMax; x++)
        {
            SpawnProp(
                objects,
                $"prop.bridge-plank-{x}",
                "Bridge Plank",
                new GridPosition(x, BridgeY),
                height: BridgeDeckHeight,
                gravity: false,
                solid: false,
                footprint: new ObjectFootprint(
                    WorldUnitsPerTile,
                    WorldUnitsPerTile));
        }
    }

    /// <summary>
    /// Advances one fixed physics tick. The caller owns the tick rate; the
    /// world only guarantees that each tick is one committed, valid step.
    /// </summary>
    public IReadOnlyList<PhysicsEvent> AdvancePhysics()
    {
        var result = Physics.Advance();
        foreach (var landing in result.Events)
        {
            if (landing.Kind == PhysicsEventKind.Landed &&
                landing.ObjectId == PlayerId &&
                landing.FallDistance > 0)
            {
                LastMessage =
                    $"You land {landing.FallDistance} units below.";
            }
        }

        return result.Events;
    }

    /// <summary>
    /// Destroys the prop trestle so everything resting on it loses its support.
    /// This is the demo's support-removal case.
    /// </summary>
    public SliceActionResult RemoveTrestleSupport()
    {
        var trestle = Map.QueryAll()
            .Where(value => value.TypeId == "prop.trestle")
            .Cast<WorldObject?>()
            .FirstOrDefault();
        if (trestle is null)
        {
            return Finish(false, "The trestle is already gone.");
        }

        var dependants = Map.SupportedObjects(trestle.Value.Id).Count;
        Objects.Destroy(trestle.Value.Id);
        return Finish(
            true,
            dependants == 0
                ? "The trestle collapses."
                : $"The trestle collapses; {dependants} object(s) fall.");
    }

    public IReadOnlyList<WorldObject> ContentsOf(ObjectId container) =>
        Objects.GetContents(container)
            .Select(Objects.Get)
            .ToArray();

    public GridPosition GetGridPosition(ObjectId id) =>
        GridPositionOf(Objects.Get(id));

    public IReadOnlyList<WorldObject> VisibleObjects(int radiusTiles = 20)
    {
        if (radiusTiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusTiles));
        }

        return QueryNearPlayer(radiusTiles, ObjectFlags.Visible);
    }

    public SliceActionResult MovePlayer(int deltaX, int deltaY)
    {
        if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
        {
            throw new ArgumentException(
                "Movement must be exactly one cardinal grid step.");
        }

        // One swept solve decides the move; the transaction below revalidates
        // the same placement contract before anything commits.
        var sweep = Movement.Resolve(
            PlayerId,
            new Vec3i(
                deltaX * WorldUnitsPerTile,
                deltaY * WorldUnitsPerTile,
                0));
        if (!sweep.ReachedTarget)
        {
            return Finish(
                false,
                sweep.Blocker.Kind == PlacementBlockerKind.MapEdge
                    ? "The edge of the demo area blocks the way."
                    : sweep.Message);
        }

        var move = Transfers.Execute(
            [
                new ObjectTransferRequest(
                    PlayerId,
                    Player.Location,
                    ObjectLocation.OnMap(DemoMapId, sweep.ResolvedPosition)),
            ],
            [sweep.PhysicsFor(PlayerId)]);
        if (!move.Succeeded)
        {
            return Finish(false, move.Message);
        }

        if (ActiveChest is { } active &&
            PlayerPosition.ManhattanDistance(GridPositionOf(active)) > 1)
        {
            CloseActiveChest();
        }

        return Finish(true, "Moved.");
    }

    public SliceActionResult ToggleBackpack()
    {
        BackpackOpen = !BackpackOpen;
        if (!BackpackOpen)
        {
            CloseActiveChest();
        }

        return Finish(
            true,
            BackpackOpen ? "Backpack opened." : "Backpack closed.");
    }

    public SliceActionResult ToggleNearestChest()
    {
        var playerPosition = PlayerPosition;
        var nearest = QueryNearPlayer(1, ObjectFlags.Container)
            .Where(candidate =>
                candidate.Id != PlayerId &&
                !candidate.HasFlag(ObjectFlags.Monster))
            .Where(chest =>
                playerPosition.ManhattanDistance(GridPositionOf(chest)) <= 1)
            .OrderBy(chest =>
                playerPosition.ManhattanDistance(GridPositionOf(chest)))
            .ThenBy(chest => chest.Id)
            .Cast<WorldObject?>()
            .FirstOrDefault();

        if (nearest is null)
        {
            return Finish(false, "Stand next to a chest or corpse first.");
        }

        var chest = nearest.Value;
        if (chest.Id == _activeChestId)
        {
            CloseActiveChest();
            return Finish(true, $"{chest.Name} closed.");
        }

        CloseActiveChest();
        Objects.SetContainerOpen(chest.Id, true);
        _activeChestId = chest.Id;
        BackpackOpen = true;
        return Finish(
            true,
            $"{chest.Name} opened. Click an item to move it.");
    }

    public SliceActionResult TakeFromOpenChest(int itemIndex)
    {
        var chest = ActiveChest;
        if (chest is null)
        {
            return Finish(false, "No chest is open.");
        }

        var contents = ContentsOf(chest.Value.Id);
        if (itemIndex < 0 || itemIndex >= contents.Count)
        {
            return Finish(false, "That chest slot is empty.");
        }

        var item = contents[itemIndex];
        return FinishTransfer(
            Transfers.Execute(
                new ObjectTransferRequest(
                    item.Id,
                    item.Location,
                    ObjectLocation.InContainer(PlayerId))),
            $"Took {item.Name}.");
    }

    public SliceActionResult PutInOpenChest(int itemIndex)
    {
        var chest = ActiveChest;
        if (chest is null)
        {
            return Finish(false, "No chest is open.");
        }

        var backpack = BackpackItems;
        if (itemIndex < 0 || itemIndex >= backpack.Count)
        {
            return Finish(false, "That backpack slot is empty.");
        }

        var destination = chest.Value;
        var item = backpack[itemIndex];
        return FinishTransfer(
            Transfers.Execute(
                new ObjectTransferRequest(
                    item.Id,
                    item.Location,
                    ObjectLocation.InContainer(destination.Id))),
            $"Stored {item.Name}.");
    }

    public WorldObject? EquippedIn(EquipmentSlot slot)
    {
        foreach (var item in EquippedItems)
        {
            if (item.Location.Slot == (byte)slot)
            {
                return item;
            }
        }

        return null;
    }

    public SliceActionResult EquipFromBackpack(
        int itemIndex,
        EquipmentSlot slot = EquipmentSlot.MainHand)
    {
        var backpack = BackpackItems;
        if (itemIndex < 0 || itemIndex >= backpack.Count)
        {
            return Finish(false, "That backpack slot is empty.");
        }

        var item = backpack[itemIndex];
        return FinishTransfer(
            Transfers.Execute(
                new ObjectTransferRequest(
                    item.Id,
                    item.Location,
                    ObjectLocation.Equipped(PlayerId, slot))),
            $"Equipped {item.Name}.");
    }

    public SliceActionResult UnequipToBackpack(EquipmentSlot slot)
    {
        var equipped = EquippedIn(slot);
        if (equipped is null)
        {
            return Finish(false, "That equipment slot is empty.");
        }

        var item = equipped.Value;
        return FinishTransfer(
            Transfers.Execute(
                new ObjectTransferRequest(
                    item.Id,
                    item.Location,
                    ObjectLocation.InContainer(PlayerId))),
            $"Unequipped {item.Name}.");
    }

    public SliceActionResult ToggleMainHand()
    {
        if (EquippedIn(EquipmentSlot.MainHand) is not null)
        {
            return UnequipToBackpack(EquipmentSlot.MainHand);
        }

        var candidate = BackpackItems
            .Select((item, index) => (Item: item, Index: index))
            .FirstOrDefault(pair =>
                pair.Item.EquipmentSlots.Accepts(
                    (byte)EquipmentSlot.MainHand));
        return candidate.Item.Id.IsNone
            ? Finish(false, "There is no main-hand item in the backpack.")
            : EquipFromBackpack(candidate.Index, EquipmentSlot.MainHand);
    }

    public SliceActionResult DropFromBackpack(int itemIndex = 0)
    {
        var backpack = BackpackItems;
        if (itemIndex < 0 || itemIndex >= backpack.Count)
        {
            return Finish(false, "There is nothing in the backpack to drop.");
        }

        var item = backpack[itemIndex];
        return FinishTransfer(
            Transfers.Execute(
                new ObjectTransferRequest(
                    item.Id,
                    item.Location,
                    Player.Location)),
            $"Dropped {item.Name}.");
    }

    public SliceActionResult PickUpAtPlayerFeet()
    {
        var item = Map.QueryAnchor(
                Player.Location.Position,
                ObjectFlags.Item)
            .OrderBy(candidate => candidate.Id)
            .Cast<WorldObject?>()
            .FirstOrDefault();
        if (item is null)
        {
            return Finish(false, "There is nothing here to pick up.");
        }

        return FinishTransfer(
            Transfers.Execute(
                new ObjectTransferRequest(
                    item.Value.Id,
                    item.Value.Location,
                    ObjectLocation.InContainer(PlayerId))),
            $"Picked up {item.Value.Name}.");
    }

    public SliceActionResult AttackAdjacentMonster()
    {
        var playerPosition = PlayerPosition;
        var target = QueryNearPlayer(1, ObjectFlags.Monster)
            .Where(candidate => candidate.IsAlive)
            .Where(candidate =>
                playerPosition.ManhattanDistance(
                    GridPositionOf(candidate)) <= 1)
            .OrderBy(candidate => candidate.Health)
            .ThenBy(candidate => candidate.Id)
            .Cast<WorldObject?>()
            .FirstOrDefault();

        if (target is null)
        {
            return Finish(false, "No living monster is in melee reach.");
        }

        var monster = target.Value;
        var remaining = Objects.Damage(monster.Id, PlayerAttackDamage);
        if (remaining > 0)
        {
            return Finish(
                true,
                $"Hit {monster.Name} for {PlayerAttackDamage} damage " +
                $"({remaining}/{monster.MaxHealth} HP).");
        }

        Objects.Transform(
            monster.Id,
            $"remains.{monster.TypeId}",
            $"Remains of {monster.Name}",
            "container.corpse",
            ObjectFlags.Container |
            ObjectFlags.Corpse |
            ObjectFlags.Usable |
            ObjectFlags.Visible,
            height: 24);
        return Finish(
            true,
            $"{monster.Name} dies. Its remains can be looted.");
    }

    public SliceActionResult ClosePanels()
    {
        BackpackOpen = false;
        CloseActiveChest();
        return Finish(true, "Closed.");
    }

    private void CloseActiveChest()
    {
        if (!_activeChestId.IsNone &&
            Objects.TryGet(_activeChestId, out var active) &&
            active.HasFlag(ObjectFlags.Container))
        {
            Objects.SetContainerOpen(_activeChestId, false);
        }

        _activeChestId = ObjectId.None;
    }

    private static void SpawnChest(
        ObjectStore objects,
        string typeId,
        string name,
        GridPosition position,
        IEnumerable<string> items)
    {
        var chest = objects.Create(new ObjectSpawn
        {
            TypeId = typeId,
            Name = name,
            ShapeId = "container.chest",
            Location = MapLocation(position),
            Footprint = new ObjectFootprint(128, 128),
            Height = 40,
            Flags =
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Usable |
                ObjectFlags.Visible,
            ContainerCapacity = 10,
        });
        foreach (var item in items)
        {
            SpawnItem(objects, chest, ItemTypeId(item), item);
        }
    }

    private static void SpawnMonster(
        ObjectStore objects,
        string typeId,
        string name,
        string shapeId,
        GridPosition position,
        int maxHealth,
        IEnumerable<string> loot)
    {
        var monster = objects.Create(new ObjectSpawn
        {
            TypeId = typeId,
            Name = name,
            ShapeId = shapeId,
            Location = MapLocation(position),
            Footprint = shapeId == "monster.many-eyed"
                ? new ObjectFootprint(160, 160)
                : new ObjectFootprint(128, 128),
            Height = shapeId == "monster.many-eyed" ? 104 : 56,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Monster |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            ContainerCapacity = 10,
            Health = maxHealth,
            MaxHealth = maxHealth,
        });
        foreach (var item in loot)
        {
            SpawnItem(objects, monster, ItemTypeId(item), item);
        }
    }

    private static void SpawnProp(
        ObjectStore objects,
        string typeId,
        string name,
        GridPosition position,
        int height,
        int baseZ = 0,
        bool gravity = true,
        bool solid = true,
        ObjectFootprint? footprint = null)
    {
        var location = MapLocation(position);
        objects.Create(new ObjectSpawn
        {
            TypeId = typeId,
            Name = name,
            ShapeId = "container.chest",
            Location = ObjectLocation.OnMap(
                DemoMapId,
                location.Position with { Z = baseZ }),
            Footprint = footprint ?? new ObjectFootprint(128, 128),
            Height = height,
            Flags =
                ObjectFlags.Visible |
                ObjectFlags.Movable |
                (solid ? ObjectFlags.Solid : ObjectFlags.ProvidesSupport) |
                (gravity ? ObjectFlags.AffectedByGravity : ObjectFlags.None),
        });
    }

    private static void SpawnWorldItem(
        ObjectStore objects,
        string name,
        GridPosition position,
        int baseZ)
    {
        var location = MapLocation(position);
        objects.Create(new ObjectSpawn
        {
            TypeId = ItemTypeId(name),
            Name = name,
            ShapeId = "loot.generic",
            Location = ObjectLocation.OnMap(
                DemoMapId,
                location.Position with { Z = baseZ }),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.AffectedByGravity |
                ObjectFlags.Visible,
        });
    }

    private static void SpawnItem(
        ObjectStore objects,
        ObjectId parent,
        string typeId,
        string name,
        string shapeId = "loot.generic",
        EquipmentSlotMask equipmentSlots = EquipmentSlotMask.None)
    {
        objects.Create(new ObjectSpawn
        {
            TypeId = typeId,
            Name = name,
            ShapeId = shapeId,
            Location = ObjectLocation.InContainer(parent),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Visible,
            EquipmentSlots = equipmentSlots,
        });
    }

    /// <summary>
    /// A footprint is anchored at the far corner of the cell it occupies, so
    /// the anchor of cell <c>c</c> is <c>(c + 1) * WorldUnitsPerTile</c>. That
    /// keeps every demo footprint inside the map's world bounds.
    /// </summary>
    private static ObjectLocation MapLocation(GridPosition position) =>
        ObjectLocation.OnMap(
            DemoMapId,
            new Vec3i(
                (position.X + 1) * WorldUnitsPerTile,
                (position.Y + 1) * WorldUnitsPerTile,
                0));

    private static GridPosition GridPositionOf(WorldObject value)
    {
        if (value.Location.Kind != LocationKind.OnMap)
        {
            throw new InvalidOperationException(
                $"Object {value.Id} is not on a map.");
        }

        var position = value.Location.Position;
        return new GridPosition(
            (position.X / WorldUnitsPerTile) - 1,
            (position.Y / WorldUnitsPerTile) - 1);
    }

    private static void BuildTerrain(WorldMap map)
    {
        var wall = new TerrainCell(FloorZ: 0, TerrainFlags.Solid);
        for (var y = 2; y <= 6; y++)
        {
            map.SetTerrain(2, y, wall);
        }

        for (var x = 12; x <= 18; x++)
        {
            map.SetTerrain(x, 25, wall);
        }

        // Stairs rising one level per cell into a raised stone platform.
        for (var y = TerraceYMin; y <= TerraceYMax; y++)
        {
            for (var x = StairsXMin; x <= StairsXMax; x++)
            {
                map.SetTerrain(
                    x,
                    y,
                    Floor((x - StairsXMin + 1) * UnitsPerLevel));
            }

            for (var x = PlatformXMin; x <= PlatformXMax; x++)
            {
                map.SetTerrain(x, y, Floor(PlatformZ));
            }
        }

        // A lower floor objects fall to, crossed by the bridge deck.
        for (var y = PitYMin; y <= PitYMax; y++)
        {
            for (var x = PitXMin; x <= PitXMax; x++)
            {
                map.SetTerrain(x, y, Floor(PitFloorZ));
            }
        }
    }

    /// <summary>
    /// Runs the world forward until nothing is falling, so a new demo starts
    /// from a settled, invariant-checked state instead of mid-air.
    /// </summary>
    private void Settle()
    {
        const int MaxSettleTicks = 400;
        for (var tick = 0; tick < MaxSettleTicks; tick++)
        {
            Physics.Advance();
            if (Objects.Enumerate().All(value =>
                value.Motion == MotionState.Resting))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"The demo world did not settle within {MaxSettleTicks} ticks.");
    }

    private static TerrainCell Floor(int floorZ) =>
        new(floorZ, TerrainFlags.Walkable | TerrainFlags.ProvidesSupport);

    private static string ItemTypeId(string name) =>
        $"item.{string.Concat(
            name.ToLowerInvariant().Select(character =>
                char.IsAsciiLetterOrDigit(character) ? character : '-'))}";

    private IReadOnlyList<WorldObject> QueryNearPlayer(
        int radiusTiles,
        ObjectFlags requiredFlags)
    {
        var center = Player.Location.Position;
        var radius = checked(radiusTiles * WorldUnitsPerTile);
        return Map.Query(
            new WorldRectangle(
                checked(center.X - radius),
                checked(center.X + radius + 1),
                checked(center.Y - radius),
                checked(center.Y + radius + 1)),
            requiredFlags);
    }

    private SliceActionResult Finish(bool succeeded, string message)
    {
        LastMessage = message;
        return new SliceActionResult(succeeded, message);
    }

    private SliceActionResult FinishTransfer(
        ObjectTransferResult transfer,
        string successMessage) =>
        Finish(
            transfer.Succeeded,
            transfer.Succeeded ? successMessage : transfer.Message);
}

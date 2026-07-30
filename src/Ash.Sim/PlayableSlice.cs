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
public sealed class PlayableSliceWorld : IDisposable
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

    /// <summary>
    /// Identifies the content this world's type and shape ids belong to. A save
    /// written for different content is refused rather than reinterpreted.
    /// </summary>
    public const string ContentFingerprint = "ash.playable-slice.v1";

    private const string AvatarTypeId = "actor.avatar";
    private const string GoldTypeId = "item.gold";

    /// <summary>The Avatar's strength, and so a 12-slot pack.</summary>
    private const int AvatarStrength = 12;


    private const int TableHeight = 40;
    private const int CrateHeight = 32;
    private const int BridgeDeckHeight = 4;

    private ObjectId _activeChestId = ObjectId.None;

    private PlayableSliceWorld(
        ObjectStore objects,
        WorldMap map,
        ObjectId playerId,
        long startTick)
    {
        Objects = objects;
        Transfers = new ObjectTransferService(objects);
        Movement = new MovementSolver(objects);
        PlayerId = playerId;
        Map = map;
        Physics = new PhysicsSystem(objects, startTick: startTick);
        SaveGate = new WorldSaveGate(objects, Physics, ContentFingerprint);
        Drag = new DragService(objects);
        Stacks = new StackService(objects);
        LastMessage = "Explore. Open a chest or fight a monster.";
    }

    public ObjectStore Objects { get; }

    public ObjectTransferService Transfers { get; }

    public MovementSolver Movement { get; }

    public PhysicsSystem Physics { get; }

    public WorldSaveGate SaveGate { get; }

    public DragService Drag { get; }

    public StackService Stacks { get; }

    /// <summary>The object currently held by the cursor, if any.</summary>
    public WorldObject? HeldObject =>
        Drag.IsDragging && Objects.TryGet(Drag.State.ObjectId, out var value)
            ? value
            : null;

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

    /// <summary>Gear slots the Avatar can carry: max(Strength, 10).</summary>
    public int BackpackCapacity => Player.CarryCapacity;

    /// <summary>Gear slots the carried inventory is using.</summary>
    public int BackpackSlotsUsed => GearSlots.UsedBy(BackpackItems);

    /// <summary>
    /// The carried inventory laid out one entry per thing, sized in gear slots,
    /// which is what the carried panel draws cells from.
    /// </summary>
    public IReadOnlyList<GearSlots.GearSlotEntry> BackpackSlots =>
        GearSlots.LayOut(BackpackItems);

    public static PlayableSliceWorld CreateDemo()
    {
        var objects = new ObjectStore();
        var player = objects.Create(new ObjectSpawn
        {
            TypeId = AvatarTypeId,
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
            Strength = AvatarStrength,
            Health = 12,
            MaxHealth = 12,
        });
        SpawnItem(
            objects,
            player,
            "item.rusty-sword",
            "Rusty Sword",
            "loot.shortsword",
            EquipmentSlotMask.RightHand);
        SpawnItem(objects, player, "item.apple", "Apple");

        SpawnChest(
            objects,
            "container.store-room",
            "Store-room Chest",
            new GridPosition(8, 13),
            ["Health Tonic", "Iron Key"],
            gold: 12);
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
            ["Silver Mirror", "Incense"],
            gold: 18);
        SpawnChest(
            objects,
            "container.vault-box",
            "Vault Box",
            new GridPosition(37, 12),
            ["Star Sapphire", "Antidote"]);
        SpawnCountedGoods(objects);

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
            loot: ["Copper Ring", "Throwing Knife"],
            worn: "Notched Blade");
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
        var map = new WorldMap(objects, DemoMapId, MapWidth, MapHeight);
        BuildTerrain(map);
        var world = new PlayableSliceWorld(objects, map, player, startTick: 0);
        world.Settle();
        return world;
    }

    /// <summary>
    /// Asks for the whole object world — objects, physics state and terrain —
    /// to be written to <paramref name="path"/>. The save happens now if the
    /// world is at a safe point, and otherwise at the next safe tick; either
    /// way the status line says which.
    /// </summary>
    public SliceActionResult RequestSave(string path)
    {
        var attempt = SaveGate.Request(path, DemoMapId);
        return Finish(attempt.Saved, attempt.Message);
    }

    /// <summary>
    /// Loads a world saved by <see cref="Save"/>. The loaded world is built
    /// beside the caller's and is never settled: a mid-fall object resumes its
    /// fall, and the physics tick continues where it stopped. Settling belongs
    /// to <see cref="CreateDemo"/>, which starts a world from nothing.
    /// </summary>
    public static PlayableSliceWorld Load(string path)
    {
        var loaded = ObjectWorldSave.RestoreFile(path, ContentFingerprint);
        var map = loaded.Objects.Maps.Get(loaded.CurrentMapId);
        var avatars = loaded.Objects.Enumerate()
            .Where(value => value.TypeId == AvatarTypeId)
            .ToArray();
        if (avatars.Length != 1)
        {
            throw new InvalidOperationException(
                $"A demo save must hold exactly one {AvatarTypeId}, but this " +
                $"one holds {avatars.Length}.");
        }

        return new PlayableSliceWorld(
            loaded.Objects,
            map,
            avatars[0].Id,
            loaded.SimulationTick);
    }

    /// <summary>
    /// Picks an object up with the cursor. Every drag and drop below is the
    /// Avatar reaching for something, so the same reach and placement rules
    /// apply as to any other transfer.
    /// </summary>
    public SliceActionResult BeginDrag(ObjectId target) =>
        FromDrag(Drag.Begin(PlayerId, target));

    public SliceActionResult DropDragOnMap(GridPosition cell)
    {
        var anchor = MapLocation(cell).Position;
        return FromDrag(Drag.DropOnMap(DemoMapId, anchor.X, anchor.Y));
    }

    public SliceActionResult DropDragInBackpack() =>
        FromDrag(Drag.DropInContainer(PlayerId));

    public SliceActionResult DropDragOnRightHand() =>
        FromDrag(Drag.DropOnEquipment(PlayerId, EquipmentSlot.RightHand));

    public SliceActionResult DropDragInOpenChest() =>
        ActiveChest is { } chest
            ? FromDrag(Drag.DropInContainer(chest.Id))
            : Finish(false, "No chest is open.");

    public SliceActionResult CancelDrag() => FromDrag(Drag.Cancel());

    /// <summary>
    /// Surfaces something that happened outside the world — a save adopted, a
    /// file that could not be read — in the status line.
    /// </summary>
    public SliceActionResult Report(string message) => Finish(true, message);

    public SliceActionResult ReportFailure(string message) =>
        Finish(false, message);

    /// <summary>
    /// Releases this world's map from its object store. A replaced world must
    /// be disposed so its map stops indexing a store nobody is using.
    /// </summary>
    public void Dispose()
    {
        SaveGate.Cancel();
        Map.Dispose();
    }

    /// <summary>
    /// The physical acceptance area: a table holding loot, a stack of crates, a
    /// bridge deck over a lower floor, and a trestle whose support the player
    /// can remove with <see cref="RemoveTrestleSupport"/>.
    /// </summary>
    /// <summary>
    /// Goods measured by the slotful, in the chests that hold them.
    /// </summary>
    private static void SpawnCountedGoods(ObjectStore objects)
    {
        var chests = objects.Enumerate()
            .Where(value =>
                value.TypeId.StartsWith("container.", StringComparison.Ordinal))
            .OrderBy(value => value.Id)
            .ToArray();
        SpawnStack(
            objects,
            chests[1].Id,
            "item.silver",
            "Silver Coins",
            40,
            GearSlots.CoinsPerSlot,
            GearSlots.CoinGroup);
        SpawnStack(
            objects,
            chests[1].Id,
            "item.arrow",
            "Arrows",
            20,
            GearSlots.AmmunitionPerSlot);
        SpawnStack(
            objects,
            chests[3].Id,
            "item.ration",
            "Rations",
            3,
            GearSlots.RationsPerSlot);
        SpawnStack(
            objects,
            chests[3].Id,
            "item.gem",
            "Cut Gems",
            6,
            GearSlots.GemsPerSlot,
            GearSlots.GemGroup);
    }

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

        // End of a completed tick: the one point a deferred save is safe.
        if (SaveGate.Flush(DemoMapId) is { } attempt && attempt.Saved)
        {
            LastMessage = attempt.Message;
        }

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

        return Carry(contents[itemIndex], "Took");
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
        return FinishStack(
            Stacks.TransferQuantity(
                item.Id,
                item.Quantity,
                ObjectLocation.InContainer(destination.Id)),
            $"Stored {Describe(item)}.");
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

    /// <summary>
    /// Equips a specific carried object, which is what a grid of gear slots
    /// clicks with: cells address objects, not row numbers.
    /// </summary>
    public SliceActionResult EquipFromBackpack(
        ObjectId itemId,
        EquipmentSlot? slot = null)
    {
        if (!Objects.TryGet(itemId, out var item) ||
            item.Location != ObjectLocation.InContainer(PlayerId))
        {
            return Finish(false, "That is not in your pack.");
        }

        var target = slot ?? FirstAcceptedSlot(item);
        if (target is null)
        {
            return Finish(false, $"{item.Name} is not something you wear.");
        }

        return FinishTransfer(
            Transfers.Execute(
                new ObjectTransferRequest(
                    itemId,
                    item.Location,
                    ObjectLocation.Equipped(PlayerId, target.Value))),
            $"Equipped {item.Name}.");
    }

    /// <summary>The first body slot an item accepts, in paper-doll order.</summary>
    private static EquipmentSlot? FirstAcceptedSlot(WorldObject item)
    {
        foreach (var slot in EquipmentSlots.All)
        {
            if (item.EquipmentSlots.Accepts((byte)slot))
            {
                return slot;
            }
        }

        return null;
    }

    public SliceActionResult EquipFromBackpack(
        int itemIndex,
        EquipmentSlot slot = EquipmentSlot.RightHand)
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

    public SliceActionResult ToggleRightHand()
    {
        if (EquippedIn(EquipmentSlot.RightHand) is not null)
        {
            return UnequipToBackpack(EquipmentSlot.RightHand);
        }

        var candidate = BackpackItems
            .Select((item, index) => (Item: item, Index: index))
            .FirstOrDefault(pair =>
                pair.Item.EquipmentSlots.Accepts(
                    (byte)EquipmentSlot.RightHand));
        return candidate.Item.Id.IsNone
            ? Finish(false, "There is no main-hand item in the backpack.")
            : EquipFromBackpack(candidate.Index, EquipmentSlot.RightHand);
    }

    public SliceActionResult DropFromBackpack(int itemIndex = 0)
    {
        var backpack = BackpackItems;
        if (itemIndex < 0 || itemIndex >= backpack.Count)
        {
            return Finish(false, "There is nothing in the backpack to drop.");
        }

        var item = backpack[itemIndex];
        return FinishStack(
            Stacks.TransferQuantity(item.Id, item.Quantity, Player.Location),
            $"Dropped {Describe(item)}.");
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

        return Carry(item.Value, "Picked up");
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

        // The body becomes one container holding everything it had, worn gear
        // included; anything that will not fit spills onto the ground.
        var corpse = Death.MakeCorpse(
            Objects,
            monster.Id,
            $"remains.{monster.TypeId}",
            $"Remains of {monster.Name}",
            "container.corpse",
            ObjectFlags.Container |
            ObjectFlags.Corpse |
            ObjectFlags.Usable |
            ObjectFlags.Visible,
            height: 24);
        if (!corpse.Succeeded)
        {
            return Finish(false, corpse.Message);
        }

        return Finish(
            true,
            corpse.Spilled.Count == 0
                ? $"{monster.Name} dies. Its remains can be looted."
                : $"{monster.Name} dies, scattering " +
                  $"{corpse.Spilled.Count} of its things.");
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
        IEnumerable<string> items,
        int gold = 0)
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
            SlotCapacity = 10,
        });
        foreach (var item in items)
        {
            SpawnItem(objects, chest, ItemTypeId(item), item);
        }

        if (gold > 0)
        {
            SpawnStack(
                objects,
                chest,
                GoldTypeId,
                "Gold Coins",
                gold,
                GearSlots.CoinsPerSlot,
                GearSlots.CoinGroup);
        }
    }

    /// <summary>
    /// Goods counted by the slotful: a hundred coins, ten gems, twenty arrows,
    /// three rations. One object carries the count, so two finds of gold become
    /// one purse rather than filling the pack with singles, and coins of any
    /// denomination share their slot.
    /// </summary>
    private static void SpawnStack(
        ObjectStore objects,
        ObjectId parent,
        string typeId,
        string name,
        int quantity,
        int perSlot,
        string group = "")
    {
        objects.Create(new ObjectSpawn
        {
            TypeId = typeId,
            Name = name,
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(parent),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Stackable |
                ObjectFlags.AffectedByGravity |
                ObjectFlags.Visible,
            Quantity = quantity,
            MaxQuantity = perSlot,
            QuantityPerSlot = perSlot,
            SlotGroup = group,
        });
    }

    private static void SpawnMonster(
        ObjectStore objects,
        string typeId,
        string name,
        string shapeId,
        GridPosition position,
        int maxHealth,
        IEnumerable<string> loot,
        string? worn = null)
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
            Strength = 10,
            Health = maxHealth,
            MaxHealth = maxHealth,
        });
        foreach (var item in loot)
        {
            SpawnItem(objects, monster, ItemTypeId(item), item);
        }

        if (worn is not null)
        {
            objects.Create(new ObjectSpawn
            {
                TypeId = ItemTypeId(worn),
                Name = worn,
                ShapeId = "loot.shortsword",
                Location = ObjectLocation.Equipped(
                    monster,
                    EquipmentSlot.RightHand),
                Footprint = new ObjectFootprint(32, 32),
                Height = 8,
                Flags = ObjectFlags.Item | ObjectFlags.Movable,
                EquipmentSlots = EquipmentSlotMask.RightHand,
            });
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

    private SliceActionResult FromDrag(DragResult result) =>
        Finish(result.Succeeded, result.Message);

    /// <summary>
    /// Where goods bound for the pack actually end up. Gear slots are a hard
    /// limit, and the rule for exceeding them is not "you cannot take it" but
    /// "it lands at your feet" — so a full pack never blocks looting, it just
    /// leaves the overflow on the floor.
    /// </summary>
    private ObjectLocation CarryDestination(WorldObject item)
    {
        var pack = ObjectLocation.InContainer(PlayerId);

        // Topping up a stack already in the pack costs no extra slot, and
        // goods that share a slot — coins of any mix — are pooled by the same
        // rule the capacity check uses.
        var projected = BackpackItems.Append(item with { Location = pack });
        if (!Stacks.FindMergeTarget(item, pack).IsNone ||
            GearSlots.UsedBy(projected) <= BackpackCapacity)
        {
            return pack;
        }

        return Player.Location;
    }

    private SliceActionResult Carry(WorldObject item, string verb)
    {
        var destination = CarryDestination(item);
        var result = Stacks.TransferQuantity(
            item.Id,
            item.Quantity,
            destination);
        if (!result.Succeeded)
        {
            return Finish(false, result.Message);
        }

        return Finish(
            true,
            destination.Kind == LocationKind.InContainer
                ? $"{verb} {Describe(item)}."
                : $"{verb} {Describe(item)}, but your pack is full: it falls " +
                  "at your feet.");
    }

    /// <summary>
    /// Every arrival of goods goes through the stack rules, so a purse joins
    /// the purse already there whether it was clicked, dragged or dropped.
    /// </summary>
    private SliceActionResult FinishStack(
        StackResult result,
        string successMessage) =>
        Finish(
            result.Succeeded,
            result.Succeeded ? successMessage : result.Message);

    private static string Describe(WorldObject value) =>
        value.Quantity > 1 ? $"{value.Quantity} {value.Name}" : value.Name;

    private SliceActionResult FinishTransfer(
        ObjectTransferResult transfer,
        string successMessage) =>
        Finish(
            transfer.Succeeded,
            transfer.Succeeded ? successMessage : transfer.Message);
}

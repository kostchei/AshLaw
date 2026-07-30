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
    public const int WorldUnitsPerTile = 256;
    public const int PlayerAttackDamage = 2;

    private ObjectId _activeChestId = ObjectId.None;

    private PlayableSliceWorld(ObjectStore objects, ObjectId playerId)
    {
        Objects = objects;
        PlayerId = playerId;
        LastMessage = "Explore. Open a chest or fight a monster.";
    }

    public ObjectStore Objects { get; }

    public ObjectId PlayerId { get; }

    public WorldObject Player => Objects.Get(PlayerId);

    public GridPosition PlayerPosition => GridPositionOf(Player);

    public int PlayerHealth => Player.Health;

    public int PlayerMaxHealth => Player.MaxHealth;

    public bool BackpackOpen { get; private set; }

    public string LastMessage { get; private set; }

    public IReadOnlyList<WorldObject> Chests =>
        Objects.Enumerate()
            .Where(candidate =>
                candidate.Id != PlayerId &&
                candidate.HasFlag(ObjectFlags.Container) &&
                !candidate.HasFlag(ObjectFlags.Monster) &&
                candidate.Location.Kind == LocationKind.OnMap)
            .ToArray();

    public IReadOnlyList<WorldObject> Monsters =>
        Objects.Enumerate()
            .Where(candidate =>
                candidate.HasFlag(ObjectFlags.Monster) &&
                candidate.IsAlive &&
                candidate.Location.Kind == LocationKind.OnMap)
            .ToArray();

    public WorldObject? ActiveChest =>
        _activeChestId.IsNone
            ? null
            : Objects.TryGet(_activeChestId, out var active)
                ? active
                : null;

    public IReadOnlyList<WorldObject> BackpackItems =>
        ContentsOf(PlayerId);

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
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            ContainerCapacity = 12,
            Health = 12,
            MaxHealth = 12,
        });
        SpawnItem(objects, player, "item.rusty-sword", "Rusty Sword", "loot.shortsword");
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

        return new PlayableSliceWorld(objects, player);
    }

    public IReadOnlyList<WorldObject> ContentsOf(ObjectId container) =>
        Objects.GetContents(container)
            .Select(Objects.Get)
            .ToArray();

    public GridPosition GetGridPosition(ObjectId id) =>
        GridPositionOf(Objects.Get(id));

    public SliceActionResult MovePlayer(int deltaX, int deltaY)
    {
        if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
        {
            throw new ArgumentException(
                "Movement must be exactly one cardinal grid step.");
        }

        var destination = PlayerPosition.Offset(deltaX, deltaY);
        if (destination.X < 0 || destination.X >= MapWidth ||
            destination.Y < 0 || destination.Y >= MapHeight)
        {
            return Finish(false, "The edge of the demo area blocks the way.");
        }

        var blocker = Objects.Enumerate().FirstOrDefault(candidate =>
            candidate.Id != PlayerId &&
            candidate.HasFlag(ObjectFlags.Solid) &&
            candidate.Location.Kind == LocationKind.OnMap &&
            candidate.Location.MapId == DemoMapId &&
            GridPositionOf(candidate) == destination);
        if (!blocker.Id.IsNone)
        {
            return Finish(false, $"{blocker.Name} occupies that space.");
        }

        Objects.Move(PlayerId, MapLocation(destination));
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
        var nearest = Chests
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

        if (BackpackItems.Count >= BackpackCapacity)
        {
            return Finish(false, "The backpack is full.");
        }

        var item = contents[itemIndex];
        Objects.Move(item.Id, ObjectLocation.InContainer(PlayerId));
        return Finish(true, $"Took {item.Name}.");
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
        if (ContentsOf(destination.Id).Count >= destination.ContainerCapacity)
        {
            return Finish(false, $"{destination.Name} is full.");
        }

        var item = backpack[itemIndex];
        Objects.Move(item.Id, ObjectLocation.InContainer(destination.Id));
        return Finish(true, $"Stored {item.Name}.");
    }

    public SliceActionResult AttackAdjacentMonster()
    {
        var playerPosition = PlayerPosition;
        var target = Monsters
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

    private static void SpawnItem(
        ObjectStore objects,
        ObjectId parent,
        string typeId,
        string name,
        string shapeId = "loot.generic")
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
        });
    }

    private static ObjectLocation MapLocation(GridPosition position) =>
        ObjectLocation.OnMap(
            DemoMapId,
            new Vec3i(
                position.X * WorldUnitsPerTile,
                position.Y * WorldUnitsPerTile,
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
            position.X / WorldUnitsPerTile,
            position.Y / WorldUnitsPerTile);
    }

    private static string ItemTypeId(string name) =>
        $"item.{string.Concat(
            name.ToLowerInvariant().Select(character =>
                char.IsAsciiLetterOrDigit(character) ? character : '-'))}";

    private SliceActionResult Finish(bool succeeded, string message)
    {
        LastMessage = message;
        return new SliceActionResult(succeeded, message);
    }
}

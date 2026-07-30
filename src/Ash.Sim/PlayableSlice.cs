namespace Ash.Sim;

public readonly record struct GridPosition(int X, int Y)
{
    public GridPosition Offset(int deltaX, int deltaY) => new(X + deltaX, Y + deltaY);

    public int ManhattanDistance(GridPosition other) =>
        Math.Abs(X - other.X) + Math.Abs(Y - other.Y);
}

public sealed record SliceActionResult(bool Succeeded, string Message);

public sealed class Inventory
{
    private readonly List<string> _items;

    public Inventory(int capacity, IEnumerable<string>? items = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        Capacity = capacity;
        _items = items?.ToList() ?? [];
        if (_items.Count > Capacity)
        {
            throw new ArgumentException(
                $"Inventory contains {_items.Count} items but holds only {Capacity}.",
                nameof(items));
        }

        if (_items.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Inventory item names must not be empty.", nameof(items));
        }
    }

    public int Capacity { get; }

    public IReadOnlyList<string> Items => _items;

    public bool IsFull => _items.Count >= Capacity;

    public bool TryAdd(string item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item);
        if (IsFull)
        {
            return false;
        }

        _items.Add(item);
        return true;
    }

    public string RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var item = _items[index];
        _items.RemoveAt(index);
        return item;
    }
}

public sealed class ChestState
{
    public ChestState(
        string id,
        string name,
        GridPosition position,
        Inventory inventory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(inventory);

        Id = id;
        Name = name;
        Position = position;
        Inventory = inventory;
    }

    public string Id { get; }

    public string Name { get; }

    public GridPosition Position { get; }

    public Inventory Inventory { get; }

    public bool IsOpen { get; internal set; }
}

public sealed class MonsterState
{
    private readonly string[] _loot;

    public MonsterState(
        string id,
        string name,
        GridPosition position,
        int maxHealth,
        IEnumerable<string> loot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (maxHealth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxHealth),
                maxHealth,
                "Monster health must be positive.");
        }

        Id = id;
        Name = name;
        Position = position;
        MaxHealth = maxHealth;
        Health = maxHealth;
        _loot = loot.ToArray();
    }

    public string Id { get; }

    public string Name { get; }

    public GridPosition Position { get; }

    public int MaxHealth { get; }

    public int Health { get; private set; }

    public bool IsAlive => Health > 0;

    public IReadOnlyList<string> Loot => _loot;

    internal void TakeDamage(int damage)
    {
        if (damage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(damage));
        }

        Health = Math.Max(0, Health - damage);
    }
}

/// <summary>
/// A deliberately small, headless gameplay slice: movement, a backpack,
/// lootable chests, melee attacks, and lootable monster remains.
/// </summary>
public sealed class PlayableSliceWorld
{
    public const int MapWidth = 41;
    public const int MapHeight = 29;
    public const int PlayerAttackDamage = 2;

    private readonly List<ChestState> _chests;
    private readonly List<MonsterState> _monsters;
    private string? _activeChestId;

    private PlayableSliceWorld(
        GridPosition playerPosition,
        Inventory backpack,
        IEnumerable<ChestState> chests,
        IEnumerable<MonsterState> monsters)
    {
        PlayerPosition = playerPosition;
        Backpack = backpack;
        _chests = chests.ToList();
        _monsters = monsters.ToList();
        LastMessage = "Explore. Open a chest or fight a monster.";
    }

    public GridPosition PlayerPosition { get; private set; }

    public int PlayerHealth { get; private set; } = 12;

    public int PlayerMaxHealth { get; } = 12;

    public Inventory Backpack { get; }

    public bool BackpackOpen { get; private set; }

    public string LastMessage { get; private set; }

    public IReadOnlyList<ChestState> Chests => _chests;

    public IReadOnlyList<MonsterState> Monsters => _monsters;

    public ChestState? ActiveChest =>
        _activeChestId is null
            ? null
            : _chests.SingleOrDefault(chest => chest.Id == _activeChestId);

    public static PlayableSliceWorld CreateDemo() =>
        new(
            new GridPosition(4, 14),
            new Inventory(12, ["Rusty Sword", "Apple"]),
            [
                new ChestState(
                    "store-room",
                    "Store-room Chest",
                    new GridPosition(8, 13),
                    new Inventory(10, ["Health Tonic", "Iron Key", "12 Gold"])),
                new ChestState(
                    "old-coffer",
                    "Old Coffer",
                    new GridPosition(21, 8),
                    new Inventory(10, ["Moonstone", "Bandage"])),
                new ChestState(
                    "pilgrims-cache",
                    "Pilgrim's Cache",
                    new GridPosition(31, 21),
                    new Inventory(10, ["Silver Mirror", "Incense", "18 Gold"])),
                new ChestState(
                    "vault-box",
                    "Vault Box",
                    new GridPosition(37, 12),
                    new Inventory(10, ["Star Sapphire", "Antidote"])),
            ],
            [
                new MonsterState(
                    "cave-rat",
                    "Cave Rat",
                    new GridPosition(13, 14),
                    maxHealth: 4,
                    loot: ["Rat Tail"]),
                new MonsterState(
                    "goblin-scout",
                    "Goblin Scout",
                    new GridPosition(24, 18),
                    maxHealth: 6,
                    loot: ["Copper Ring", "Throwing Knife"]),
                new MonsterState(
                    "many-eyed-tyrant",
                    "Many-Eyed Tyrant",
                    new GridPosition(35, 8),
                    maxHealth: 8,
                    loot: ["Glass Eye", "Nullstone Shard"]),
                new MonsterState(
                    "goblin-guard",
                    "Goblin Guard",
                    new GridPosition(30, 22),
                    maxHealth: 8,
                    loot: ["Iron Buckle", "Guard Token"]),
            ]);

    public SliceActionResult MovePlayer(int deltaX, int deltaY)
    {
        if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
        {
            throw new ArgumentException("Movement must be exactly one cardinal grid step.");
        }

        var destination = PlayerPosition.Offset(deltaX, deltaY);
        if (destination.X < 0 || destination.X >= MapWidth ||
            destination.Y < 0 || destination.Y >= MapHeight)
        {
            return Finish(false, "The edge of the demo area blocks the way.");
        }

        if (_chests.Any(chest => chest.Position == destination) ||
            _monsters.Any(monster => monster.IsAlive && monster.Position == destination))
        {
            return Finish(false, "Something occupies that space.");
        }

        PlayerPosition = destination;
        if (ActiveChest is { } active &&
            PlayerPosition.ManhattanDistance(active.Position) > 1)
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
        var nearest = _chests
            .Where(chest => PlayerPosition.ManhattanDistance(chest.Position) <= 1)
            .OrderBy(chest => PlayerPosition.ManhattanDistance(chest.Position))
            .ThenBy(chest => chest.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (nearest is null)
        {
            return Finish(false, "Stand next to a chest or corpse first.");
        }

        if (nearest.Id == _activeChestId)
        {
            CloseActiveChest();
            return Finish(true, $"{nearest.Name} closed.");
        }

        CloseActiveChest();
        nearest.IsOpen = true;
        _activeChestId = nearest.Id;
        BackpackOpen = true;
        return Finish(true, $"{nearest.Name} opened. Click an item to move it.");
    }

    public SliceActionResult TakeFromOpenChest(int itemIndex)
    {
        var chest = ActiveChest;
        if (chest is null)
        {
            return Finish(false, "No chest is open.");
        }

        if (itemIndex < 0 || itemIndex >= chest.Inventory.Items.Count)
        {
            return Finish(false, "That chest slot is empty.");
        }

        var item = chest.Inventory.Items[itemIndex];
        if (!Backpack.TryAdd(item))
        {
            return Finish(false, "The backpack is full.");
        }

        chest.Inventory.RemoveAt(itemIndex);
        return Finish(true, $"Took {item}.");
    }

    public SliceActionResult PutInOpenChest(int itemIndex)
    {
        var chest = ActiveChest;
        if (chest is null)
        {
            return Finish(false, "No chest is open.");
        }

        if (itemIndex < 0 || itemIndex >= Backpack.Items.Count)
        {
            return Finish(false, "That backpack slot is empty.");
        }

        var item = Backpack.Items[itemIndex];
        if (!chest.Inventory.TryAdd(item))
        {
            return Finish(false, $"{chest.Name} is full.");
        }

        Backpack.RemoveAt(itemIndex);
        return Finish(true, $"Stored {item}.");
    }

    public SliceActionResult AttackAdjacentMonster()
    {
        var monster = _monsters
            .Where(candidate =>
                candidate.IsAlive &&
                PlayerPosition.ManhattanDistance(candidate.Position) <= 1)
            .OrderBy(candidate => candidate.Health)
            .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (monster is null)
        {
            return Finish(false, "No living monster is in melee reach.");
        }

        monster.TakeDamage(PlayerAttackDamage);
        if (monster.IsAlive)
        {
            return Finish(
                true,
                $"Hit {monster.Name} for {PlayerAttackDamage} damage " +
                $"({monster.Health}/{monster.MaxHealth} HP).");
        }

        var corpse = new ChestState(
            $"remains-{monster.Id}",
            $"Remains of {monster.Name}",
            monster.Position,
            new Inventory(10, monster.Loot));
        _chests.Add(corpse);
        return Finish(true, $"{monster.Name} dies. Its remains can be looted.");
    }

    public SliceActionResult ClosePanels()
    {
        BackpackOpen = false;
        CloseActiveChest();
        return Finish(true, "Closed.");
    }

    private void CloseActiveChest()
    {
        if (ActiveChest is { } active)
        {
            active.IsOpen = false;
        }

        _activeChestId = null;
    }

    private SliceActionResult Finish(bool succeeded, string message)
    {
        LastMessage = message;
        return new SliceActionResult(succeeded, message);
    }
}

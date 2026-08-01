using Ash.Core;
using Ash.Rules;

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
    /// <summary>
    /// The hand-built acceptance map. Generated subzones are numbered from one,
    /// so the demo never collides with a world's own maps.
    /// </summary>
    public const ushort DemoMapId = 0;

    public const int DemoMapWidth = 41;
    public const int DemoMapHeight = 29;
    public const int WorldUnitsPerTile = WorldMap.WorldUnitsPerTile;
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
    public const string ContentFingerprint = "ash.playable-slice.v2";

    private const string AvatarTypeId = "actor.avatar";
    private const string DaggerTypeId = "item.bronze-dagger";

    /// <summary>The demo's dice seed, so a fresh demo always plays the same.</summary>
    private const ulong DefaultSeed = 20260731;
    private const string GoldTypeId = "item.gold";

    /// <summary>The Avatar's strength, and so a 12-slot pack.</summary>
    private const int AvatarStrength = 12;

    /// <summary>
    /// Keeps the Avatar's body off the world's roll stream. His hit dice are
    /// rolled before the world exists, so rolling them from the world seed
    /// directly would make the first monster's recognition delay depend on how
    /// many dice his class happens to take.
    /// </summary>
    private const ulong AvatarBodySalt = 0x9E3779B97F4A7C15UL;

    /// <summary>
    /// What a world is generated against. Difficulty and treasure scale from
    /// the character a world is made for, and a world is made for a fresh one.
    /// </summary>
    private static readonly CharacterTier StartingTier =
        new(Level: 1, CharacterClass.Fighter);

    /// <summary>
    /// The Avatar's scores, until character creation rolls them. This is the
    /// seam: <see cref="CharacterCreation"/> already produces a
    /// <see cref="CreatedCharacter"/> carrying exactly these, and nothing here
    /// derives anything from a literal that a created character would not also
    /// supply.
    /// </summary>
    private static readonly AbilityScores AvatarScores = new(
        strength: AvatarStrength,
        dexterity: 12,
        constitution: 14,
        intelligence: 10,
        wisdom: 10,
        charisma: 12);

    private const int TableHeight = 40;
    private const int CrateHeight = 32;
    private const int BridgeDeckHeight = 4;

    private ObjectId _activeChestId = ObjectId.None;

    private PlayableSliceWorld(
        ObjectStore objects,
        ObjectId playerId,
        long startTick,
        ulong diceState,
        IAttackRulesResolver? attackResolver = null)
    {
        Dice = Dice.FromState(diceState);
        Objects = objects;
        Transfers = new ObjectTransferService(objects);
        Movement = new MovementSolver(objects);
        Transitions = new MapTransitionService(objects);
        PlayerId = playerId;
        Physics = new PhysicsSystem(objects, startTick: startTick);

        // The combat beat is read off the physics tick, so a loaded world
        // resumes the fight's pacing on the same beat it was saved on.
        Clock = new CombatClock(startPhysicsTick: startTick);
        Conditions = new ActorConditionService();
        Vitality = new ActorVitality(
            objects,
            RulesRepository.Vitality,
            RulesRepository.AbilityBonuses,
            Dice,
            Conditions);
        Sheets = new ActorSheets(
            objects,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);
        Trauma = new TraumaEffectDispatcher(
            objects, Transfers, Vitality, Conditions, Movement, () => Clock.Tick);
        Attacks = new CombatAttackService(
            objects,
            Sheets,
            Vitality,
            Dice,
            attackResolver ?? new RulesAttackRulesResolver(),
            Conditions,
            () => Clock.Tick,
            Trauma);
        Combat = new CombatDirector(
            objects,
            Movement,
            Transfers,
            Clock,
            Dice,
            Vitality,
            Attacks,
            Conditions,
            playerId);
        SaveGate = new WorldSaveGate(
            objects, Physics, Dice, ContentFingerprint, Conditions);
        Drag = new DragService(objects);
        Stacks = new StackService(objects);
        LastMessage = "Explore. Open a chest or fight a monster.";
    }

    public ObjectStore Objects { get; }

    public ObjectTransferService Transfers { get; }

    public MovementSolver Movement { get; }

    /// <summary>Carries a body from the map it is on to another one.</summary>
    public MapTransitionService Transitions { get; }

    public PhysicsSystem Physics { get; }

    public WorldSaveGate SaveGate { get; }

    /// <summary>The 200 ms beat combat is scheduled on.</summary>
    public CombatClock Clock { get; }

    /// <summary>Who has noticed whom, and when anyone may next swing.</summary>
    public CombatDirector Combat { get; }

    /// <summary>The shared player and NPC melee-resolution path.</summary>
    public CombatAttackService Attacks { get; }

    /// <summary>Persistent combat effects currently active on actors.</summary>
    public ActorConditionService Conditions { get; }

    public TraumaEffectDispatcher Trauma { get; }

    /// <summary>
    /// What the last advanced beat produced — alerts to sound, blows to draw.
    /// Empty on every tick that did not complete a beat.
    /// </summary>
    public IReadOnlyList<CombatEvent> LastCombatEvents { get; private set; } =
        [];

    public DragService Drag { get; }

    public StackService Stacks { get; }

    /// <summary>
    /// The world's dice. Seeded, saved and resumed, so a loaded world rolls
    /// what it would have rolled.
    /// </summary>
    public Dice Dice { get; }

    /// <summary>Attack and defence derived from scores, class and worn gear.</summary>
    public ActorSheets Sheets { get; }

    /// <summary>
    /// The only thing in the world that hurts or heals a body. Concussion
    /// hits, the wound layer under them and the death clock under that all
    /// resolve here.
    /// </summary>
    public ActorVitality Vitality { get; }

    /// <summary>How hurt the Avatar is.</summary>
    public InjuryState PlayerInjury => Vitality.Of(PlayerId);

    public ActorSheet PlayerSheet => Sheets.For(PlayerId);

    /// <summary>The object currently held by the cursor, if any.</summary>
    public WorldObject? HeldObject =>
        Drag.IsDragging && Objects.TryGet(Drag.State.ObjectId, out var value)
            ? value
            : null;

    /// <summary>
    /// The map the world is currently being played on: whichever one the Avatar
    /// is standing on.
    /// </summary>
    /// <remarks>
    /// Deliberately derived rather than stored. A world holds every map it has
    /// built, and there is exactly one thing that decides which of them is the
    /// one being looked at, walked through and drawn — where the Avatar is. A
    /// second copy of that fact would only be a second thing to get wrong on a
    /// transition or a load.
    /// </remarks>
    public ushort CurrentMapId
    {
        get
        {
            var player = Player;
            if (player.Location.Kind != LocationKind.OnMap)
            {
                throw new InvalidOperationException(
                    $"{player.Name} is not on a map.");
            }

            return player.Location.MapId;
        }
    }

    public WorldMap CurrentMap => Objects.Maps.Get(CurrentMapId);

    public ObjectId PlayerId { get; }

    public WorldObject Player => Objects.Get(PlayerId);

    public GridPosition PlayerPosition => GridPositionOf(Player);

    public int PlayerHealth => Player.Health;

    public int PlayerMaxHealth => Player.MaxHealth;

    public bool BackpackOpen { get; private set; }

    public string LastMessage { get; private set; }

    public IReadOnlyList<WorldObject> Chests =>
        CurrentMap.QueryAll(ObjectFlags.Container)
            .Where(candidate =>
                candidate.Id != PlayerId &&
                !candidate.HasFlag(ObjectFlags.Monster))
            .ToArray();

    public IReadOnlyList<WorldObject> Monsters =>
        CurrentMap.QueryAll(ObjectFlags.Monster)
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
        CurrentMap.QueryAll(ObjectFlags.Item);

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

    /// <summary>
    /// A fresh demo world. The seed is the whole of its chance: the same seed
    /// always plays the same, which is what lets a test pin a 1d6 recognition
    /// delay to a number instead of a range it hopes for.
    /// </summary>
    public static PlayableSliceWorld CreateDemo(
        ulong seed = DefaultSeed,
        IAttackRulesResolver? attackResolver = null)
    {
        var objects = new ObjectStore();
        var player = SpawnAvatar(
            objects,
            DemoMapLocation(new GridPosition(4, 14)),
            RollAvatarBody(seed));

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
        SpawnFinesseWeapon(objects);

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
        var map = new WorldMap(objects, DemoMapId, DemoMapWidth, DemoMapHeight);
        try
        {
            BuildTerrain(map);
            var world = new PlayableSliceWorld(
                objects,
                player,
                startTick: 0,
                seed,
                attackResolver);
            world.Settle();
            return world;
        }
        catch
        {
            // A map left registered with the store would be indexed on every
            // later commit and would show up in a save.
            map.Dispose();
            throw;
        }
    }

    /// <summary>
    /// A fresh generated world: the whole eighteen-subzone plan for
    /// <paramref name="worldSeed"/>, built, with the Avatar standing at the
    /// first subzone's way in.
    /// </summary>
    /// <remarks>
    /// Every subzone is built up front and every map is kept. Eighteen maps of
    /// 65 by at most 29 cells is a few hundred kilobytes, which buys something
    /// worth having: a transition is a move between two maps that already
    /// exist, so it cannot fail halfway through building the far side, and the
    /// save is the whole world rather than the room the player happens to be
    /// standing in. Building lazily is a change to make when a world is too big
    /// to hold, not before.
    /// </remarks>
    public static PlayableSliceWorld CreateGenerated(ulong worldSeed)
    {
        if (worldSeed == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldSeed),
                worldSeed,
                "A world seed of zero would leave the dice with no state to " +
                "start from.");
        }

        var plan = WorldPlanner.Plan(worldSeed, StartingTier);
        var objects = new ObjectStore();
        var built = new List<WorldMap>(plan.Subzones.Count);
        try
        {
            foreach (var subzone in plan.Subzones)
            {
                built.Add(SubzoneBuilder.Build(objects, subzone).Map);
            }

            var first = plan.Subzones[0];
            var (cellX, cellY) = WorldPlanner.EntranceCell(first);
            var player = SpawnAvatar(
                objects,
                ObjectLocation.OnMap(
                    first.MapId,
                    AnchorOf(
                        new GridPosition(cellX, cellY),
                        first.Beats[0].FloorZ)),
                RollAvatarBody(worldSeed));
            var world = new PlayableSliceWorld(
                objects,
                player,
                startTick: 0,
                worldSeed);
            world.Settle();
            world.LastMessage =
                $"You enter {first.Theme}. Find the way on.";
            return world;
        }
        catch
        {
            foreach (var map in built)
            {
                map.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Asks for the whole object world — objects, physics state and terrain —
    /// to be written to <paramref name="path"/>. The save happens now if the
    /// world is at a safe point, and otherwise at the next safe tick; either
    /// way the status line says which.
    /// </summary>
    public SliceActionResult RequestSave(string path)
    {
        var attempt = SaveGate.Request(path, CurrentMapId);
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
        var avatars = loaded.Objects.Enumerate()
            .Where(value => value.TypeId == AvatarTypeId)
            .ToArray();
        if (avatars.Length != 1)
        {
            throw new InvalidOperationException(
                $"A demo save must hold exactly one {AvatarTypeId}, but this " +
                $"one holds {avatars.Length}.");
        }

        // The save names the map that was being played, and the Avatar stands
        // on one. Two answers to one question is one too many, so a save that
        // disagrees with itself is refused rather than half-adopted.
        var avatar = avatars[0];
        if (avatar.Location.Kind != LocationKind.OnMap ||
            avatar.Location.MapId != loaded.CurrentMapId)
        {
            throw new InvalidOperationException(
                $"The save was taken on map {loaded.CurrentMapId} but its " +
                $"{AvatarTypeId} is at {avatar.Location}.");
        }

        var world = new PlayableSliceWorld(
            loaded.Objects,
            avatar.Id,
            loaded.SimulationTick,
            loaded.DiceState);
        foreach (var condition in loaded.Conditions ?? [])
        {
            if (!loaded.Objects.TryGet(condition.ActorId, out var actor) ||
                !actor.HasFlag(ObjectFlags.Actor))
            {
                world.Dispose();
                throw new ObjectWorldSaveException(
                    $"Condition {condition.Kind} targets absent actor {condition.ActorId}.");
            }
        }
        world.Conditions.Restore(loaded.Conditions ?? []);
        return world;
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
        var anchor = AnchorOf(cell);
        return FromDrag(Drag.DropOnMap(CurrentMapId, anchor.X, anchor.Y));
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
    /// Releases every map this world built from its object store. A replaced
    /// world must be disposed so its maps stop indexing a store nobody is
    /// using.
    /// </summary>
    public void Dispose()
    {
        SaveGate.Cancel();

        // Every map, not just the one being played: a generated world holds
        // eighteen, and any left registered would keep indexing a store
        // nothing else is using.
        foreach (var map in Objects.Maps.All)
        {
            map.Dispose();
        }
    }

    /// <summary>
    /// The body the Avatar walks in with: one hit die per level carrying his
    /// constitution bonus, over a wound pool of level plus whatever he is best
    /// at. Rolled rather than authored, so a d10 fighter and a d4 wizard are
    /// not the same person with a different hat.
    /// </summary>
    private static RolledBody RollAvatarBody(ulong seed) =>
        ActorVitality.RollBody(
            RulesRepository.Vitality,
            RulesRepository.AbilityBonuses,
            new Dice(seed ^ AvatarBodySalt),
            StartingTier.Class,
            StartingTier.Level,
            AvatarScores);

    /// <summary>
    /// The Avatar and what he starts out holding, wherever the world puts him.
    /// </summary>
    private static ObjectId SpawnAvatar(
        ObjectStore objects,
        ObjectLocation location,
        RolledBody body)
    {
        var player = objects.Create(new ObjectSpawn
        {
            TypeId = AvatarTypeId,
            Name = "Avatar",
            ShapeId = "avatar.knight",
            Location = location,
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            StepHeight = StepHeightUnits,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.AffectedByGravity |
                ObjectFlags.Visible,
            Strength = AvatarScores.Strength,
            Dexterity = AvatarScores.Dexterity,
            Constitution = AvatarScores.Constitution,
            Intelligence = AvatarScores.Intelligence,
            Wisdom = AvatarScores.Wisdom,
            Charisma = AvatarScores.Charisma,
            Class = StartingTier.Class,
            Level = StartingTier.Level,
            Health = body.MaximumConcussion,
            MaxHealth = body.MaximumConcussion,
            Wounds = body.MaximumWounds,
            MaxWounds = body.MaximumWounds,
        });
        SpawnItem(
            objects,
            player,
            "item.rusty-sword",
            "Rusty Sword",
            "loot.shortsword",
            EquipmentSlotMask.RightHand,
            ObjectFlags.Weapon,
            quality: -1);
        SpawnItem(objects, player, "item.apple", "Apple");
        return player;
    }

    /// <summary>
    /// A light blade: finesse weapons are swung with dexterity when that serves
    /// the wielder better than strength.
    /// </summary>
    private static void SpawnFinesseWeapon(ObjectStore objects)
    {
        var chest = objects.Enumerate()
            .First(value => value.TypeId == "container.old-coffer");
        objects.Create(new ObjectSpawn
        {
            TypeId = DaggerTypeId,
            Name = "Bronze Dagger",
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.InContainer(chest.Id),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Finesse |
                ObjectFlags.Weapon |
                ObjectFlags.Visible,
            EquipmentSlots =
                EquipmentSlotMask.EitherHand | EquipmentSlotMask.Scabbard,
            Quality = -1,
        });
    }

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

        // Combat is scheduled on the coarser 200 ms beat: recognition, alerts
        // and swings all land on one, so a fight paces the same on any machine
        // whatever the frame rate happens to be.
        if (Clock.Advance(Physics.Tick))
        {
            foreach (var periodic in Conditions.AdvanceTo(Clock.Tick))
            {
                if (Objects.TryGet(periodic.ActorId, out var actor) &&
                    actor.IsAlive && actor.Injury.IsUpright && periodic.Damage > 0)
                {
                    _ = Vitality.Damage(periodic.ActorId, periodic.Damage);
                    if (periodic.ActorId == PlayerId)
                    {
                        LastMessage = $"You bleed for {periodic.Damage} damage.";
                    }
                }
            }
            LastCombatEvents = Combat.Advance();
        }
        else
        {
            LastCombatEvents = [];
        }
        foreach (var combat in LastCombatEvents)
        {
            LastMessage = combat.Message;
        }

        // End of a completed tick: the one point a deferred save is safe.
        if (SaveGate.Flush(CurrentMapId) is { } attempt && attempt.Saved)
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
        var trestle = CurrentMap.QueryAll()
            .Where(value => value.TypeId == "prop.trestle")
            .Cast<WorldObject?>()
            .FirstOrDefault();
        if (trestle is null)
        {
            return Finish(false, "The trestle is already gone.");
        }

        var dependants = CurrentMap.SupportedObjects(trestle.Value.Id).Count;
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
        if (Conditions.PreventsMovement(PlayerId))
        {
            return Finish(false, "You cannot move while incapacitated.");
        }

        if (Math.Abs(deltaX) + Math.Abs(deltaY) != 1)
        {
            throw new ArgumentException(
                "Movement must be exactly one cardinal grid step.");
        }

        // The round holds a maximum move as well as a maximum number of
        // attacks. The budget is checked before the solve and only spent on a
        // step that actually happens, so walking into a wall costs nothing.
        if (!Combat.PlayerCanStep)
        {
            return Finish(
                false,
                "You have covered as much ground as the round allows.");
        }

        // One swept solve decides the move; the transaction below revalidates
        // the same placement contract before anything commits.
        var displacement = new Vec3i(
            deltaX * WorldUnitsPerTile,
            deltaY * WorldUnitsPerTile,
            0);

        // Walk first, at the height anything clears without trying. Only a
        // move actually stopped by something worth getting over is a vault, so
        // a stroll across flat floor never touches the dice.
        var sweep = Movement.Resolve(
            PlayerId,
            displacement,
            VaultCheck.MinimumStepHeight);
        var vault = (VaultCheckResult?)null;
        if (!sweep.ReachedTarget && IsVaultable(sweep.Blocker))
        {
            var rolled = VaultCheck.Roll(
                Dice,
                PlayerSheet.Abilities,
                Player.Class,
                RulesRepository.AbilityBonuses,
                Player.Impairments);
            vault = rolled;
            sweep = Movement.Resolve(PlayerId, displacement, rolled.StepHeight);
        }

        if (!sweep.ReachedTarget)
        {
            return Finish(
                false,
                sweep.Blocker.Kind == PlacementBlockerKind.MapEdge
                    ? "The edge of the demo area blocks the way."
                    : vault is { } failed
                        ? $"You try to get over it and fall short " +
                          $"({DescribeVault(failed)})."
                        : sweep.Message);
        }

        var move = Transfers.Execute(
            [
                new ObjectTransferRequest(
                    PlayerId,
                    Player.Location,
                    ObjectLocation.OnMap(CurrentMapId, sweep.ResolvedPosition)),
            ],
            [sweep.PhysicsFor(PlayerId)]);
        if (!move.Succeeded)
        {
            return Finish(false, move.Message);
        }

        var step = Combat.TryPlayerStep();
        if (!step.Stepped)
        {
            throw new InvalidOperationException(
                "The move committed after the step budget was checked but " +
                "before it could be spent.");
        }

        if (ActiveChest is { } active &&
            PlayerPosition.ManhattanDistance(GridPositionOf(active)) > 1)
        {
            CloseActiveChest();
        }

        return Finish(
            true,
            vault is { } cleared
                ? $"You vault it ({DescribeVault(cleared)})."
                : "Moved.");
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

    /// <summary>
    /// The one "use what I am standing by" command: a chest if there is one,
    /// and otherwise the way on or the way back.
    /// </summary>
    public SliceActionResult Interact()
    {
        var chest = ToggleNearestChest();
        if (chest.Succeeded)
        {
            return chest;
        }

        var travelled = UseNearestWaymark();
        return travelled.Succeeded
            ? travelled
            : Finish(
                false,
                "Stand next to a chest, a corpse, or a way through first.");
    }

    /// <summary>
    /// The ways in and out of the map the Avatar is on.
    /// </summary>
    public IReadOnlyList<WorldObject> Waymarks =>
        CurrentMap.QueryAll()
            .Where(IsWaymark)
            .OrderBy(candidate => candidate.Id)
            .ToArray();

    /// <summary>
    /// Steps through the way on, or the way back, that the Avatar is standing
    /// on or beside.
    /// </summary>
    public SliceActionResult UseNearestWaymark()
    {
        var here = PlayerPosition;
        var nearest = QueryNearPlayer(1, ObjectFlags.Visible)
            .Where(IsWaymark)
            .Where(mark => here.ManhattanDistance(GridPositionOf(mark)) <= 1)
            .OrderBy(mark => here.ManhattanDistance(GridPositionOf(mark)))
            .ThenBy(mark => mark.Id)
            .Cast<WorldObject?>()
            .FirstOrDefault();
        return nearest is null
            ? Finish(false, "There is no way through from here.")
            : Travel(nearest.Value);
    }

    /// <summary>
    /// Carries the Avatar through <paramref name="waymark"/> to the matching
    /// one on the neighbouring subzone.
    /// </summary>
    /// <remarks>
    /// Subzone <c>n</c> is map <c>n + 1</c>, so the neighbour is one map id
    /// away, and the arrival cell is wherever that map's own waymark stands.
    /// Both ends are objects in the world, which is why this works identically
    /// on a world that was just generated and on one that was loaded from a
    /// save: nothing here needs the plan that made them.
    /// </remarks>
    private SliceActionResult Travel(WorldObject waymark)
    {
        var onward = waymark.TypeId == SubzoneBuilder.ExitTypeId;
        var fromMapId = waymark.Location.MapId;
        var toMapId = checked((ushort)(onward ? fromMapId + 1 : fromMapId - 1));
        if (!Objects.Maps.TryGet(toMapId, out var destination))
        {
            return Finish(
                false,
                $"{waymark.Name} leads to map {toMapId}, which this world " +
                "has not built.");
        }

        var arrivalTypeId = onward
            ? SubzoneBuilder.EntranceTypeId
            : SubzoneBuilder.ExitTypeId;
        var arrival = destination.QueryAll()
            .Where(candidate => candidate.TypeId == arrivalTypeId)
            .OrderBy(candidate => candidate.Id)
            .Cast<WorldObject?>()
            .FirstOrDefault();
        if (arrival is null)
        {
            throw new InvalidOperationException(
                $"Map {toMapId} holds no {arrivalTypeId} for {waymark.Name} " +
                "to come out at.");
        }

        var cell = GridPositionOf(arrival.Value);
        var moved = Transitions.Move(PlayerId, toMapId, cell.X, cell.Y);
        if (!moved.Succeeded)
        {
            return Finish(false, moved.Message);
        }

        // Panels are about the room that was left. A chest two subzones back is
        // not something the Avatar still has his hand in.
        CloseActiveChest();
        return Finish(
            true,
            onward
                ? $"You take the way on into subzone {toMapId}."
                : $"You go back through to subzone {toMapId}.");
    }

    private static bool IsWaymark(WorldObject value) =>
        value.TypeId == SubzoneBuilder.EntranceTypeId ||
        value.TypeId == SubzoneBuilder.ExitTypeId;

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
        var item = CurrentMap.QueryAnchor(
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
        if (Conditions.PreventsAction(PlayerId))
        {
            return Finish(false, "You cannot attack while incapacitated.");
        }

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

        // The round gates the blow, not the keypress: a swing is only spent
        // when it actually falls on something. Walking is free inside the round
        // — MovePlayer is deliberately not gated — so the six seconds are a
        // window to move and strike in, not a window of standing still.
        var monster = target.Value;
        var swing = Combat.TryPlayerAttack(monster.Id);
        if (!swing.Swung)
        {
            return Finish(false, swing.Message);
        }

        Combat.Provoke(monster.Id);
        LastCombatEvents = Combat.DrainImmediateEvents();
        return Finish(true, swing.Message);
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
            Location = DemoMapLocation(position),
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
            Location = DemoMapLocation(position),
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
            Dexterity = 11,
            Constitution = 10,
            Intelligence = 8,
            Wisdom = 8,
            Charisma = 6,
            Class = CharacterClass.Fighter,
            Level = 1,
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
                Flags =
                    ObjectFlags.Item |
                    ObjectFlags.Movable |
                    ObjectFlags.Weapon,
                EquipmentSlots = EquipmentSlotMask.RightHand,
                Quality = -1,
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
        var location = DemoMapLocation(position);
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
        var location = DemoMapLocation(position);
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
        EquipmentSlotMask equipmentSlots = EquipmentSlotMask.None,
        ObjectFlags extraFlags = ObjectFlags.None,
        int quality = 0)
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
                extraFlags |
                ObjectFlags.Visible,
            EquipmentSlots = equipmentSlots,
            Quality = quality,
        });
    }

    /// <summary>
    /// A footprint is anchored at the far corner of the cell it occupies, so
    /// the anchor of cell <c>c</c> is <c>(c + 1) * WorldUnitsPerTile</c>. That
    /// keeps every footprint inside the map's world bounds.
    /// </summary>
    private static Vec3i AnchorOf(GridPosition position, int z = 0) =>
        new(
            checked((position.X + 1) * WorldUnitsPerTile),
            checked((position.Y + 1) * WorldUnitsPerTile),
            z);

    /// <summary>A cell of the hand-built demo map, at floor level.</summary>
    private static ObjectLocation DemoMapLocation(GridPosition position) =>
        ObjectLocation.OnMap(DemoMapId, AnchorOf(position));

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
        return CurrentMap.Query(
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

    /// <summary>
    /// Whether the thing in the way is worth trying to get over: something with
    /// a top the Avatar's best roll could reach, and higher than what he clears
    /// without trying.
    /// </summary>
    /// <remarks>
    /// This is what keeps the dice out of ordinary walking. A wall, the map
    /// edge and a stacked crate are all simply refused, because no roll changes
    /// them; and a living body is not scenery, so you go round a goblin rather
    /// than hurdling it.
    /// </remarks>
    private bool IsVaultable(PlacementBlocker blocker)
    {
        var top = TopOfBlocker(blocker);
        if (top is not { } surface)
        {
            return false;
        }

        var climb = surface - Player.Location.Position.Z;
        return climb > VaultCheck.MinimumStepHeight &&
            climb <= VaultCheck.MaximumStepHeight;
    }

    private int? TopOfBlocker(PlacementBlocker blocker)
    {
        switch (blocker.Kind)
        {
            case PlacementBlockerKind.Object:
                var obstacle = Objects.Get(blocker.ObjectId);
                return obstacle.HasFlag(ObjectFlags.Actor)
                    ? null
                    : checked(obstacle.Location.Position.Z + obstacle.Height);

            case PlacementBlockerKind.Terrain:
                // A solid cell is a wall, not a ledge: it has no top to get
                // onto however high you jump.
                var cell = CurrentMap.GetTerrain(blocker.TerrainX, blocker.TerrainY);
                return cell.Flags.HasFlag(TerrainFlags.Solid)
                    ? null
                    : cell.FloorZ;

            default:
                return null;
        }
    }

    /// <summary>
    /// A vault attempt in the status line: which ability carried it, what was
    /// rolled, and how high that got you.
    /// </summary>
    private static string DescribeVault(VaultCheckResult vault) =>
        $"{vault.Ability} {vault.Roll}{vault.Bonus:+#;-#;+0}" +
        $"{(vault.HadAdvantage ? " adv" : string.Empty)}" +
        $"{(vault.HadDisadvantage ? " dis" : string.Empty)} " +
        $"= {vault.StepHeight} high";

    private static string Describe(WorldObject value) =>
        value.Quantity > 1 ? $"{value.Quantity} {value.Name}" : value.Name;

    private SliceActionResult FinishTransfer(
        ObjectTransferResult transfer,
        string successMessage) =>
        Finish(
            transfer.Succeeded,
            transfer.Succeeded ? successMessage : transfer.Message);
}

using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

/// <summary>
/// A bare, flat world with a player and whatever monsters a test asks for.
/// </summary>
/// <remarks>
/// The demo world is a good fixture for "does the shipped content work", and a
/// poor one for "what does this profile do": its terraces, pits and props mean a
/// creature that failed to close might have been blocked rather than have
/// decided not to close. This builds open floor and nothing else, so every
/// observed difference is a decision.
/// </remarks>
internal sealed class CombatScenario : IDisposable
{
    public const int Tile = WorldMap.WorldUnitsPerTile;
    private const int PhysicsTicksPerBeat = 12;

    private readonly WorldMap _map;
    private long _physicsTick;

    public CombatScenario(
        IAttackRulesResolver? resolver = null,
        ulong seed = 20260801,
        int width = 40,
        int depth = 40)
    {
        Objects = new ObjectStore();
        PlayerId = SpawnActor("actor.avatar", "Avatar", 5, 5, health: 20, monster: false);
        _map = new WorldMap(Objects, 0, width, depth);
        Transfers = new ObjectTransferService(Objects);
        Movement = new MovementSolver(Objects);
        Clock = new CombatClock();
        Dice = new Dice(seed);
        Conditions = new ActorConditionService();
        Vitality = new ActorVitality(
            Objects,
            RulesRepository.Vitality,
            RulesRepository.AbilityBonuses,
            Dice,
            Conditions);
        Sheets = new ActorSheets(
            Objects, RulesRepository.ClassProgression, RulesRepository.AbilityBonuses);
        Trauma = new TraumaEffectDispatcher(
            Objects, Transfers, Vitality, Conditions, Movement, () => Clock.Tick, Dice);
        Attacks = new CombatAttackService(
            Objects,
            Sheets,
            Vitality,
            Dice,
            resolver ?? new HarmlessResolver(),
            Conditions,
            () => Clock.Tick,
            Trauma);
        Pathfinder = new SpatialPathfinder(Objects);
        Projectiles = new ProjectileSystem(Objects, Transfers, Clock, Attacks);
        Spells = new SpellCastingService(
            Objects, Sheets, Conditions, Clock, Projectiles,
            RulesRepository.Rules.Spellcasting);
        Behaviors = new BehaviorProfileCatalog();
        Schedules = new NpcScheduleService(
            Objects, Transfers, Pathfinder, Clock, Conditions);
        Combat = new CombatDirector(
            Objects, Movement, Transfers, Clock, Dice, Vitality, Attacks,
            Conditions, Pathfinder, Projectiles, Spells, Behaviors, PlayerId);
    }

    public ObjectStore Objects { get; }

    public ObjectTransferService Transfers { get; }

    public MovementSolver Movement { get; }

    public CombatClock Clock { get; }

    public Dice Dice { get; }

    public ActorConditionService Conditions { get; }

    public ActorVitality Vitality { get; }

    public ActorSheets Sheets { get; }

    public TraumaEffectDispatcher Trauma { get; }

    public CombatAttackService Attacks { get; }

    public SpatialPathfinder Pathfinder { get; }

    public ProjectileSystem Projectiles { get; }

    public SpellCastingService Spells { get; }

    public BehaviorProfileCatalog Behaviors { get; }

    public NpcScheduleService Schedules { get; }

    public CombatDirector Combat { get; }

    public ObjectId PlayerId { get; }

    public WorldMap Map => _map;

    public static Vec3i Anchor(int tileX, int tileY) =>
        new((tileX + 1) * Tile, (tileY + 1) * Tile, 0);

    public static int TileDistance(Vec3i a, Vec3i b) =>
        (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)) / Tile;

    public int DistanceToPlayer(ObjectId actorId) =>
        TileDistance(
            Objects.Get(actorId).Location.Position,
            Objects.Get(PlayerId).Location.Position);

    public ObjectId SpawnMonster(
        int tileX,
        int tileY,
        BehaviorProfile profile,
        string typeId = "monster.cave-rat",
        int health = 12)
    {
        var id = SpawnActor(typeId, "Test Monster", tileX, tileY, health, monster: true);
        Behaviors.Override(id, profile);
        return id;
    }

    public void PlacePlayer(int tileX, int tileY) => Place(PlayerId, tileX, tileY);

    public void Place(ObjectId actorId, int tileX, int tileY)
    {
        var moved = Transfers.Execute(new ObjectTransferRequest(
            actorId,
            Objects.Get(actorId).Location,
            ObjectLocation.OnMap(0, Anchor(tileX, tileY))));
        Assert.True(moved.Succeeded, moved.Message);
    }

    /// <summary>A solid, fixed obstacle: an ordinary object, not a nav-grid flag.</summary>
    public ObjectId SpawnWall(int tileX, int tileY) =>
        Objects.Create(new ObjectSpawn
        {
            TypeId = "test.wall",
            Name = "Wall",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, Anchor(tileX, tileY)),
            Footprint = new ObjectFootprint(Tile, Tile),
            Height = 64,
            Flags = ObjectFlags.Solid | ObjectFlags.Fixed | ObjectFlags.Visible,
        });

    public ObjectId GiveStack(ObjectId ownerId, string typeId, string name, int quantity) =>
        Objects.Create(new ObjectSpawn
        {
            TypeId = typeId,
            Name = name,
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(ownerId),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable |
                ObjectFlags.Stackable | ObjectFlags.Visible,
            Quantity = quantity,
            MaxQuantity = Math.Max(quantity, 20),
            QuantityPerSlot = 20,
        });

    public ObjectId Equip(ObjectId actorId, string typeId, string name, EquipmentSlot slot) =>
        Objects.Create(new ObjectSpawn
        {
            TypeId = typeId,
            Name = name,
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.Equipped(actorId, slot),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable |
                ObjectFlags.Weapon | ObjectFlags.Visible,
            EquipmentSlots = EquipmentSlotMask.EitherHand,
        });

    /// <summary>Advances exactly one 200 ms combat beat and returns what it produced.</summary>
    public IReadOnlyList<CombatEvent> AdvanceBeat()
    {
        _physicsTick += PhysicsTicksPerBeat;
        Assert.True(Clock.Advance(_physicsTick));
        var events = Combat.Advance();
        Schedules.Advance(Combat.IsEngagedWithPlayer);
        return events;
    }

    public IReadOnlyList<CombatEvent> AdvanceBeats(int beats)
    {
        var all = new List<CombatEvent>();
        for (var beat = 0; beat < beats; beat++)
        {
            all.AddRange(AdvanceBeat());
        }

        return all;
    }

    /// <summary>
    /// Winds the world forward to the next occurrence of a clock hour. Beat zero
    /// is dawn, so this exists to keep tests speaking in hours rather than in
    /// beats offset from six in the morning.
    /// </summary>
    public void SkipToHour(int hour)
    {
        var beat = ((hour - WorldCalendar.DawnHour + WorldCalendar.HoursPerDay) %
            WorldCalendar.HoursPerDay) * (long)WorldCalendar.BeatsPerHour;
        while (beat <= Clock.Tick)
        {
            beat += WorldCalendar.BeatsPerDay;
        }

        SkipTo(beat);
        Assert.Equal(hour, WorldCalendar.HourOf(Clock.Tick));
    }

    /// <summary>Skips time without running a fight, for schedule and dawn tests.</summary>
    public void SkipTo(long beat)
    {
        Assert.True(beat > Clock.Tick, "A world cannot be wound backwards.");
        _physicsTick = beat * PhysicsTicksPerBeat;
        Assert.True(Clock.Advance(_physicsTick));
    }

    public void Dispose() => _map.Dispose();

    private ObjectId SpawnActor(
        string typeId,
        string name,
        int tileX,
        int tileY,
        int health,
        bool monster) =>
        Objects.Create(new ObjectSpawn
        {
            TypeId = typeId,
            Name = name,
            ShapeId = "actor.knight",
            Location = ObjectLocation.OnMap(0, Anchor(tileX, tileY)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 40,
            StepHeight = 8,
            Flags = ObjectFlags.Actor | ObjectFlags.Solid | ObjectFlags.Visible |
                ObjectFlags.Container | (monster ? ObjectFlags.Monster : ObjectFlags.None),
            Strength = 12,
            Dexterity = 12,
            Constitution = 12,
            Intelligence = 10,
            Wisdom = 10,
            Charisma = 10,
            Health = health,
            MaxHealth = health,
            Wounds = 6,
            MaxWounds = 6,
        });

    /// <summary>
    /// Always hits, never harms. Behaviour tests are about what a creature
    /// decides, so nothing may die out from under the observation.
    /// </summary>
    internal sealed class HarmlessResolver : IAttackRulesResolver
    {
        public int Calls { get; private set; }

        public AttackResult Resolve(AttackRequest request)
        {
            Calls++;
            return new AttackResult
            {
                Hit = true,
                RawD20 = request.RawD20,
                NetRoll = request.RawD20,
                Margin = 1,
                ConcussionHits = 0,
                Mishap = false,
                Messages = ["scenario resolver"],
            };
        }
    }
}

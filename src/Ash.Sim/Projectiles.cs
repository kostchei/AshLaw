using Ash.Core;
using Ash.Rules;

namespace Ash.Sim;

public enum ProjectileKind : byte
{
    /// <summary>Ammunition loosed from a launcher and spent by the shot.</summary>
    Ammunition = 0,

    /// <summary>The weapon itself, thrown, and recoverable where it lands.</summary>
    ThrownWeapon = 1,

    /// <summary>A cast effect. Nothing physical is left to pick up.</summary>
    SpellBolt = 2,

    /// <summary>A cast effect that resolves against everything where it arrives.</summary>
    SpellBall = 3,
}

/// <summary>
/// What one kind of thing in flight is: how fast, how far, what it does when it
/// arrives, and what — if anything — is left afterwards.
/// </summary>
public sealed record ProjectileDefinition(
    string Id,
    string Name,
    string ShapeId,
    ProjectileKind Kind,
    AttackProfile Attack,

    /// <summary>Tiles crossed per 200 ms beat. Flight is visible, not instant.</summary>
    int SpeedTilesPerBeat,

    /// <summary>The furthest the launcher may aim.</summary>
    int RangeTiles,

    /// <summary>Tiles either side of the arrival tile also resolved against. Zero hits one target.</summary>
    int BlastRadiusTiles = 0,

    /// <summary>
    /// The item left on the ground by a flight that struck no one, or empty
    /// when the projectile leaves nothing behind (CMB-024).
    /// </summary>
    string RecoveredTypeId = "",
    string RecoveredName = "",
    string RecoveredShapeId = "")
{
    public bool IsBlast => BlastRadiusTiles > 0;

    public bool IsRecoverable => !string.IsNullOrEmpty(RecoveredTypeId);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) ||
            string.IsNullOrWhiteSpace(ShapeId))
        {
            throw new ArgumentException("A projectile needs an id, name and shape.", nameof(Id));
        }

        if (SpeedTilesPerBeat < 1 || RangeTiles < 1 || BlastRadiusTiles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedTilesPerBeat));
        }

        if (IsRecoverable &&
            (string.IsNullOrWhiteSpace(RecoveredName) ||
             string.IsNullOrWhiteSpace(RecoveredShapeId)))
        {
            throw new ArgumentException(
                "A recoverable projectile needs a recovered name and shape.",
                nameof(RecoveredTypeId));
        }

        Attack.Validate();
    }
}

/// <summary>One projectile currently crossing the room.</summary>
/// <remarks>
/// Position is not stored here: the projectile is an ordinary world object and
/// the object store owns where it is. What is stored is what the object cannot
/// say for itself — who loosed it, what it was aimed at, and how far along its
/// line it has travelled, which is what makes the flight resumable after a load
/// without re-deriving a route from a position that is only a point on it.
/// </remarks>
public readonly record struct ProjectileFlight(
    ObjectId ProjectileId,
    ObjectId ShooterId,
    ObjectId TargetId,
    string DefinitionId,
    Vec3i Origin,
    Vec3i AimPoint,
    int TilesTravelled,
    long LaunchedAtTick);

public sealed class ProjectileCatalog
{
    public const string ArrowTypeId = "item.arrow";

    /// <summary>
    /// The launcher's type id, which is also the id its attack profile is keyed
    /// by: a bow in hand and a bow's arrow in the air are one attack identity,
    /// held once in <see cref="CombatProfileCatalog"/>.
    /// </summary>
    public const string ShortBowTypeId = CombatProfileCatalog.ShortBowTypeId;

    public const string ThrowingKnifeTypeId = CombatProfileCatalog.ThrowingKnifeTypeId;

    public static readonly ProjectileDefinition Arrow = new(
        "projectile.arrow",
        "Arrow",
        "projectile.arrow",
        ProjectileKind.Ammunition,
        CombatProfileCatalog.ShortBow,
        SpeedTilesPerBeat: 6,
        RangeTiles: 18,
        RecoveredTypeId: ArrowTypeId,
        RecoveredName: "Arrow",
        RecoveredShapeId: "loot.generic");

    public static readonly ProjectileDefinition ThrowingKnife = new(
        "projectile.throwing-knife",
        "Throwing knife",
        "projectile.knife",
        ProjectileKind.ThrownWeapon,
        CombatProfileCatalog.ThrowingKnife,
        SpeedTilesPerBeat: 4,
        RangeTiles: 8,
        RecoveredTypeId: ThrowingKnifeTypeId,
        RecoveredName: "Throwing knife",
        RecoveredShapeId: "loot.shortsword");

    private readonly IReadOnlyDictionary<string, ProjectileDefinition> _definitions;

    public ProjectileCatalog(IEnumerable<ProjectileDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var index = new Dictionary<string, ProjectileDefinition>(StringComparer.Ordinal);
        foreach (var definition in definitions)
        {
            definition.Validate();
            if (!index.TryAdd(definition.Id, definition))
            {
                throw new ArgumentException(
                    $"Projectile '{definition.Id}' is authored twice.",
                    nameof(definitions));
            }
        }

        _definitions = index;
    }

    /// <summary>
    /// Every projectile the world can launch: the physical ones authored here,
    /// and one per spell that leaves the caster's hand.
    /// </summary>
    public static ProjectileCatalog Default { get; } = new(
        new[] { Arrow, ThrowingKnife }.Concat(SpellCatalog.Projectiles));

    public IReadOnlyList<ProjectileDefinition> All =>
        _definitions.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();

    public ProjectileDefinition Get(string id) =>
        _definitions.TryGetValue(id, out var definition)
            ? definition
            : throw new InvalidOperationException($"No projectile '{id}' is authored.");

    public bool TryGet(string id, out ProjectileDefinition definition) =>
        _definitions.TryGetValue(id, out definition!);

    /// <summary>What a weapon of this type puts in the air when it is used at range.</summary>
    public static ProjectileDefinition? ForLauncher(string weaponTypeId) => weaponTypeId switch
    {
        ShortBowTypeId => Arrow,
        ThrowingKnifeTypeId => ThrowingKnife,
        _ => null,
    };

    /// <summary>What a launcher of this type spends per shot, or empty for none.</summary>
    public static string AmmunitionFor(string weaponTypeId) => weaponTypeId switch
    {
        ShortBowTypeId => ArrowTypeId,
        ThrowingKnifeTypeId => ThrowingKnifeTypeId,
        _ => "",
    };
}

/// <summary>Why a flight ended.</summary>
public enum ProjectileOutcome : byte
{
    StruckActor = 0,
    StruckTerrain = 1,
    Arrived = 2,

    /// <summary>The shooter or the projectile stopped existing mid-flight.</summary>
    Lost = 3,
}

public sealed record ProjectileImpact(
    ProjectileFlight Flight,
    ProjectileOutcome Outcome,
    Vec3i Position,
    IReadOnlyList<ObjectId> Struck);

/// <summary>
/// Everything currently in the air, and what it does when it gets there.
/// </summary>
/// <remarks>
/// A projectile is an ordinary world object moved by ordinary transfers, so it
/// is drawn, saved, indexed and collided with by the same systems as a crate. It
/// is not a special effect the presentation layer owns and the simulation only
/// hears about: what decides a hit is the spatial index reporting an actor
/// overlapping the volume the projectile just moved into, which is the same
/// query anything else asks about the same space.
///
/// A flight advances in whole tiles rather than by a velocity, because a fight
/// is scheduled in whole beats and a hit that lands between two of them cannot
/// be ordered against a swing that lands on one.
/// </remarks>
public sealed class ProjectileSystem
{
    private readonly ObjectStore _objects;
    private readonly ObjectTransferService _transfers;
    private readonly CombatClock _clock;
    private readonly CombatAttackService _attacks;
    private readonly ProjectileCatalog _catalog;
    private readonly Dictionary<ObjectId, ProjectileFlight> _flights = [];

    public ProjectileSystem(
        ObjectStore objects,
        ObjectTransferService transfers,
        CombatClock clock,
        CombatAttackService attacks,
        ProjectileCatalog? catalog = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _attacks = attacks ?? throw new ArgumentNullException(nameof(attacks));
        _catalog = catalog ?? ProjectileCatalog.Default;
    }

    public ProjectileCatalog Catalog => _catalog;

    public IReadOnlyList<ProjectileFlight> InFlight =>
        _flights.Values.OrderBy(value => value.ProjectileId).ToArray();

    public bool IsInFlight(ObjectId projectileId) => _flights.ContainsKey(projectileId);

    /// <summary>
    /// Puts one projectile in the air, aimed at <paramref name="aimPoint"/>.
    /// </summary>
    /// <remarks>
    /// The aim point is committed at launch and never re-read. A shot that has
    /// left the bow does not follow a target that steps aside, which is the
    /// whole reason a projectile is worth tracking rather than resolving where
    /// it was fired.
    /// </remarks>
    public ProjectileFlight Launch(
        ObjectId shooterId,
        ObjectId targetId,
        ProjectileDefinition definition,
        Vec3i aimPoint)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var shooter = _objects.Get(shooterId);
        if (shooter.Location.Kind != LocationKind.OnMap)
        {
            throw new InvalidOperationException(
                $"{shooter.Name} is not on a map and cannot loose anything.");
        }

        var origin = shooter.Location.Position;
        if (TileDistance(origin, aimPoint) > definition.RangeTiles)
        {
            throw new InvalidOperationException(
                $"{definition.Name} carries {definition.RangeTiles} tiles, " +
                $"but was aimed {TileDistance(origin, aimPoint)} away.");
        }

        var projectileId = _objects.Create(new ObjectSpawn
        {
            TypeId = definition.Id,
            Name = definition.Name,
            ShapeId = definition.ShapeId,
            Location = ObjectLocation.OnMap(shooter.Location.MapId, origin),

            // A quarter tile: small enough to pass a gap a body cannot, large
            // enough that the spatial index reports it against the actor it
            // arrives on rather than only against an exact anchor.
            Footprint = new ObjectFootprint(
                WorldMap.WorldUnitsPerTile / 4,
                WorldMap.WorldUnitsPerTile / 4),
            Height = WorldMap.WorldUnitsPerTile / 4,

            // Not solid: a shot does not shoulder a crate out of the way, and
            // not falling: what carries it is its flight, not gravity.
            Flags = ObjectFlags.Visible,
        });

        var flight = new ProjectileFlight(
            projectileId,
            shooterId,
            targetId,
            definition.Id,
            origin,
            aimPoint,
            TilesTravelled: 0,
            _clock.Tick);
        _flights.Add(projectileId, flight);
        return flight;
    }

    /// <summary>
    /// Advances every flight one beat and reports what each one reached. The
    /// caller turns impacts into resolved attacks; this system owns only motion.
    /// </summary>
    public IReadOnlyList<ProjectileImpact> Advance()
    {
        var impacts = new List<ProjectileImpact>();
        foreach (var flight in _flights.Values.OrderBy(value => value.ProjectileId).ToArray())
        {
            if (Step(flight) is { } impact)
            {
                _flights.Remove(flight.ProjectileId);
                impacts.Add(impact);
            }
        }

        return impacts;
    }

    /// <summary>
    /// Resolves one impact into damage, and disposes of the projectile.
    /// </summary>
    /// <remarks>
    /// Every struck actor goes through the same ranged resolution path, in
    /// object-id order, so a blast that catches three creatures rolls for them
    /// in an order a save can reproduce.
    /// </remarks>
    public IReadOnlyList<CombatAttackOutcome> Resolve(ProjectileImpact impact)
    {
        ArgumentNullException.ThrowIfNull(impact);
        var definition = _catalog.Get(impact.Flight.DefinitionId);
        var outcomes = new List<CombatAttackOutcome>();
        foreach (var targetId in impact.Struck)
        {
            if (!_objects.TryGet(targetId, out var target) || !target.IsAlive ||
                !target.Injury.IsUpright)
            {
                continue;
            }

            outcomes.Add(_attacks.ResolveRanged(
                impact.Flight.ShooterId,
                targetId,
                definition.Attack));
        }

        Retire(impact, definition);
        return outcomes;
    }

    public void Forget(ObjectId projectileId) => _flights.Remove(projectileId);

    public IReadOnlyList<ProjectileFlight> Capture() => InFlight;

    public void Restore(IEnumerable<ProjectileFlight> flights)
    {
        ArgumentNullException.ThrowIfNull(flights);
        _flights.Clear();
        foreach (var flight in flights.OrderBy(value => value.ProjectileId))
        {
            if (!_catalog.TryGet(flight.DefinitionId, out var definition))
            {
                throw new ObjectWorldSaveException(
                    $"A saved projectile names unknown content '{flight.DefinitionId}'.");
            }

            if (flight.TilesTravelled < 0 ||
                flight.TilesTravelled > definition.RangeTiles ||
                flight.LaunchedAtTick < 0 ||
                !_objects.TryGet(flight.ProjectileId, out var projectile) ||
                projectile.TypeId != definition.Id ||
                !_objects.TryGet(flight.ShooterId, out _))
            {
                throw new ObjectWorldSaveException(
                    $"The saved projectile {flight.ProjectileId} is invalid.");
            }

            _flights.Add(flight.ProjectileId, flight);
        }
    }

    /// <summary>
    /// Moves one flight up to its speed in whole tiles, stopping at the first
    /// thing it reaches.
    /// </summary>
    private ProjectileImpact? Step(ProjectileFlight flight)
    {
        var definition = _catalog.Get(flight.DefinitionId);
        if (!_objects.TryGet(flight.ProjectileId, out var projectile) ||
            projectile.Location.Kind != LocationKind.OnMap)
        {
            return new ProjectileImpact(
                flight, ProjectileOutcome.Lost, flight.Origin, []);
        }

        var line = TileLine(flight.Origin, flight.AimPoint);
        var travelled = flight.TilesTravelled;
        var map = _objects.Maps.Get(projectile.Location.MapId);
        for (var step = 0; step < definition.SpeedTilesPerBeat; step++)
        {
            if (travelled >= line.Count)
            {
                return Arrival(flight with { TilesTravelled = travelled },
                    definition, map, projectile.Location.Position);
            }

            var next = line[travelled];
            var moved = _transfers.Execute(new ObjectTransferRequest(
                flight.ProjectileId,
                projectile.Location,
                ObjectLocation.OnMap(projectile.Location.MapId, next)));
            if (!moved.Succeeded)
            {
                // The one placement contract refused the tile: a wall, a closed
                // door, the map edge. That is where the shot stops.
                return new ProjectileImpact(
                    flight with { TilesTravelled = travelled },
                    ProjectileOutcome.StruckTerrain,
                    projectile.Location.Position,
                    definition.IsBlast
                        ? ActorsWithin(map, projectile.Location.Position,
                            definition.BlastRadiusTiles, flight.ShooterId)
                        : []);
            }

            travelled++;
            projectile = _objects.Get(flight.ProjectileId);
            var struck = ActorsWithin(map, next, 0, flight.ShooterId);
            if (struck.Count > 0)
            {
                return new ProjectileImpact(
                    flight with { TilesTravelled = travelled },
                    ProjectileOutcome.StruckActor,
                    next,
                    definition.IsBlast
                        ? ActorsWithin(map, next, definition.BlastRadiusTiles, flight.ShooterId)
                        : [struck[0]]);
            }

            if (travelled >= line.Count)
            {
                return Arrival(
                    flight with { TilesTravelled = travelled }, definition, map, next);
            }
        }

        _flights[flight.ProjectileId] = flight with { TilesTravelled = travelled };
        return null;
    }

    private static ProjectileImpact Arrival(
        ProjectileFlight flight,
        ProjectileDefinition definition,
        WorldMap map,
        Vec3i position) =>
        new(
            flight,
            ProjectileOutcome.Arrived,
            position,
            definition.IsBlast
                ? ActorsWithin(map, position, definition.BlastRadiusTiles, flight.ShooterId)
                : []);

    /// <summary>
    /// Takes the spent projectile out of the air: destroyed if it struck a body,
    /// and otherwise left on the ground as the item it was, where the content
    /// says there is one to pick up.
    /// </summary>
    private void Retire(ProjectileImpact impact, ProjectileDefinition definition)
    {
        if (!_objects.TryGet(impact.Flight.ProjectileId, out var projectile))
        {
            return;
        }

        var mapId = projectile.Location.Kind == LocationKind.OnMap
            ? projectile.Location.MapId
            : (ushort)0;
        var landed = projectile.Location.Kind == LocationKind.OnMap;
        _objects.Destroy(impact.Flight.ProjectileId);
        if (!definition.IsRecoverable || !landed ||
            impact.Outcome == ProjectileOutcome.StruckActor ||
            impact.Struck.Count > 0)
        {
            return;
        }

        _objects.Create(new ObjectSpawn
        {
            TypeId = definition.RecoveredTypeId,
            Name = definition.RecoveredName,
            ShapeId = definition.RecoveredShapeId,
            Location = ObjectLocation.OnMap(mapId, impact.Position),
            Footprint = new ObjectFootprint(
                WorldMap.WorldUnitsPerTile / 2,
                WorldMap.WorldUnitsPerTile / 2),
            Height = 4,
            Flags = ObjectFlags.Visible | ObjectFlags.Movable | ObjectFlags.Item |
                ObjectFlags.AffectedByGravity |
                (definition.Kind == ProjectileKind.Ammunition
                    ? ObjectFlags.Stackable
                    : ObjectFlags.Weapon),
            MaxQuantity = definition.Kind == ProjectileKind.Ammunition
                ? GearSlots.AmmunitionPerSlot
                : 1,
            QuantityPerSlot = definition.Kind == ProjectileKind.Ammunition
                ? GearSlots.AmmunitionPerSlot
                : 0,
        });
    }

    /// <summary>
    /// Living actors within <paramref name="radiusTiles"/> of a point, in
    /// object-id order, never including the shooter.
    /// </summary>
    private static IReadOnlyList<ObjectId> ActorsWithin(
        WorldMap map,
        Vec3i position,
        int radiusTiles,
        ObjectId shooterId)
    {
        var reach = checked((radiusTiles + 1) * WorldMap.WorldUnitsPerTile);
        var region = new WorldRectangle(
            checked(position.X - reach),
            checked(position.X + reach),
            checked(position.Y - reach),
            checked(position.Y + reach));
        return map.Query(region, ObjectFlags.Actor)
            .Where(value => value.Id != shooterId && value.IsAlive &&
                value.Injury.IsUpright &&
                TileDistance(value.Location.Position, position) <= radiusTiles)
            .Select(value => value.Id)
            .Order()
            .ToArray();
    }

    /// <summary>
    /// The tiles a straight shot passes through, excluding the one it starts on.
    /// </summary>
    /// <remarks>
    /// Integer Bresenham, so the line a save resumes is the line it was on.
    /// </remarks>
    private static IReadOnlyList<Vec3i> TileLine(Vec3i origin, Vec3i aim)
    {
        var fromX = origin.X / WorldMap.WorldUnitsPerTile;
        var fromY = origin.Y / WorldMap.WorldUnitsPerTile;
        var toX = aim.X / WorldMap.WorldUnitsPerTile;
        var toY = aim.Y / WorldMap.WorldUnitsPerTile;
        var deltaX = Math.Abs(toX - fromX);
        var deltaY = -Math.Abs(toY - fromY);
        var stepX = fromX < toX ? 1 : -1;
        var stepY = fromY < toY ? 1 : -1;
        var error = deltaX + deltaY;
        var line = new List<Vec3i>();
        var x = fromX;
        var y = fromY;
        while (x != toX || y != toY)
        {
            // One axis per tile, so the line is four-connected and never cuts a
            // corner a body could not shoot through. The reached-axis guards
            // make overshoot impossible, which is what bounds the loop.
            var doubled = 2 * error;
            if (x != toX && (doubled >= deltaY || y == toY))
            {
                error += deltaY;
                x += stepX;
            }
            else
            {
                error += deltaX;
                y += stepY;
            }

            line.Add(new Vec3i(
                checked(x * WorldMap.WorldUnitsPerTile),
                checked(y * WorldMap.WorldUnitsPerTile),
                aim.Z));
        }

        return line;
    }

    private static int TileDistance(Vec3i a, Vec3i b) =>
        (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)) / WorldMap.WorldUnitsPerTile;
}

using Ash.Core;
using Ash.Rules;

namespace Ash.Sim;

/// <summary>
/// One built subzone: its map, and what each beat put in the world.
/// </summary>
public sealed record SubzoneBuild(
    SubzonePlan Plan,
    WorldMap Map,
    IReadOnlyList<ObjectId> Spawned);

/// <summary>
/// Builds a <see cref="SubzonePlan"/> into terrain and objects. The builder
/// adds no chance of its own: everything it varies, it reads off the plan.
/// </summary>
/// <remarks>
/// Nothing here degrades. A beat whose content will not fit the room the plan
/// gave it throws, because the alternative — quietly skipping the hazard, or
/// dropping the reward — produces a world that passes every test and is not the
/// world the seed describes.
/// </remarks>
public static class SubzoneBuilder
{
    /// <summary>
    /// The way in: where a traveller arriving from the previous subzone lands,
    /// and the way back to it.
    /// </summary>
    public const string EntranceTypeId = "portal.entrance";

    /// <summary>The way on to the next subzone.</summary>
    public const string ExitTypeId = "portal.exit";

    /// <summary>Solid rock, which every subzone is carved out of.</summary>
    public static TerrainCell Rock => new(0, TerrainFlags.Solid);

    private const int PropHeight = 40;
    private const int ChestHeight = 40;
    private const int DecalHeight = 1;
    private const int WaymarkHeight = 1;
    private const int MonsterHeight = 56;
    private const int TileUnits = WorldMap.WorldUnitsPerTile;

    /// <summary>
    /// Carves <paramref name="plan"/> into a new map on
    /// <paramref name="objects"/> and fills its beats.
    /// </summary>
    public static SubzoneBuild Build(ObjectStore objects, SubzonePlan plan)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(plan);

        var map = new WorldMap(
            objects,
            plan.MapId,
            plan.Width,
            plan.Depth,
            defaultTerrain: Rock);
        try
        {
            CarveTerrain(map, plan);
            var spawned = new List<ObjectId>();
            foreach (var beat in plan.Beats)
            {
                spawned.AddRange(FillBeat(objects, map, plan, beat));
            }

            spawned.AddRange(SpawnWaymarks(objects, map, plan));
            return new SubzoneBuild(plan, map, spawned);
        }
        catch
        {
            // A half-carved map left registered with the store would be indexed
            // on every later commit and would show up in a save.
            map.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Every cell a body can stand on, in cell order. Used by the traversability
    /// check and by anything that needs to know where the floor is.
    /// </summary>
    public static IEnumerable<(int X, int Y)> WalkableCells(WorldMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        for (var y = 0; y < map.Depth; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (map.GetTerrain(x, y).Flags.HasFlag(TerrainFlags.Walkable))
                {
                    yield return (x, y);
                }
            }
        }
    }

    /// <summary>
    /// The way in and the way on, as objects standing on the cells they name.
    /// </summary>
    /// <remarks>
    /// A cross-map move needs to know where the traveller comes out, and the
    /// only thing that survives a save is the world itself: a plan is made from
    /// a seed nothing stores. Putting the two ends in the world means a loaded
    /// subzone can be left the same way a freshly built one can — the exit of
    /// subzone <c>n</c> looks for the entrance on map <c>n + 1</c> and finds it.
    ///
    /// The first subzone has no way back and the last has no way on, so neither
    /// gets a mark it could not lead anywhere through.
    /// </remarks>
    private static IReadOnlyList<ObjectId> SpawnWaymarks(
        ObjectStore objects,
        WorldMap map,
        SubzonePlan plan)
    {
        var marks = new List<ObjectId>();
        if (plan.Index > 0)
        {
            var (x, y) = WorldPlanner.EntranceCell(plan);
            marks.Add(
                SpawnWaymark(
                    objects,
                    map,
                    plan,
                    EntranceTypeId,
                    "Way In",
                    x,
                    y,
                    plan.Beats[0].FloorZ));
        }

        if (plan.Index < WorldPlanner.SubzoneCount - 1)
        {
            var (x, y) = WorldPlanner.ExitCell(plan);
            marks.Add(
                SpawnWaymark(
                    objects,
                    map,
                    plan,
                    ExitTypeId,
                    "Way On",
                    x,
                    y,
                    plan.Beats[^1].FloorZ));
        }

        return marks;
    }

    private static ObjectId SpawnWaymark(
        ObjectStore objects,
        WorldMap map,
        SubzonePlan plan,
        string typeId,
        string name,
        int cellX,
        int cellY,
        int floorZ) =>
        Spawn(
            objects,
            map,
            plan,
            new ObjectSpawn
            {
                TypeId = typeId,
                Name = name,
                ShapeId = "loot.generic",
                Location = Anchor(plan, cellX, cellY, floorZ),
                Footprint = new ObjectFootprint(TileUnits, TileUnits),
                Height = WaymarkHeight,

                // A threshold is scenery: it must never block the step through
                // it, and it is not something anyone picks up and carries off.
                Flags = ObjectFlags.Visible,
            });

    private static void CarveTerrain(WorldMap map, SubzonePlan plan)
    {
        foreach (var beat in plan.Beats)
        {
            var floor = FloorCell(beat.FloorZ);
            for (var y = beat.Cells.YMin; y < beat.Cells.YMax; y++)
            {
                for (var x = beat.Cells.XMin; x < beat.Cells.XMax; x++)
                {
                    map.SetTerrain(x, y, floor);
                }
            }
        }

        for (var beat = 0; beat < WorldPlanner.BeatsPerSubzone - 1; beat++)
        {
            var corridor = WorldPlanner.CorridorCells(plan, beat);
            var ramp = WorldPlanner.CorridorRamp(plan, beat);
            for (var y = corridor.YMin; y < corridor.YMax; y++)
            {
                for (var x = corridor.XMin; x < corridor.XMax; x++)
                {
                    map.SetTerrain(x, y, FloorCell(ramp[x - corridor.XMin]));
                }
            }
        }

        // The hazard beat's danger is terrain, so it is carved rather than
        // spawned. Its hint is an object, and FillBeat owes the world one.
        var hazard = plan.Beats.Single(beat => beat.Kind == BeatKind.Hazard);
        foreach (var (x, y) in HazardCells(plan, hazard))
        {
            map.SetTerrain(
                x,
                y,
                new TerrainCell(
                    hazard.FloorZ,
                    TerrainFlags.Walkable |
                    TerrainFlags.ProvidesSupport |
                    TerrainFlags.Hazard));
        }
    }

    /// <summary>
    /// The hazard's cells: a band across the room the corridor mouth opens
    /// onto, so it is crossed rather than walked round, and never the room's
    /// own entrance cell.
    /// </summary>
    private static IEnumerable<(int X, int Y)> HazardCells(
        SubzonePlan plan,
        BeatPlan hazard)
    {
        var cells = hazard.Cells;
        var x = (cells.XMin + cells.XMax) / 2;
        for (var y = plan.CorridorY - 1; y <= plan.CorridorY + 1; y++)
        {
            if (y >= cells.YMin && y < cells.YMax)
            {
                yield return (x, y);
            }
        }
    }

    private static IReadOnlyList<ObjectId> FillBeat(
        ObjectStore objects,
        WorldMap map,
        SubzonePlan plan,
        BeatPlan beat)
    {
        var centre = Centre(beat);
        return beat.Kind switch
        {
            BeatKind.Encounter =>
                [SpawnMonster(
                    objects,
                    map,
                    plan,
                    beat,
                    plan.EncounterMonsterTypeId,
                    centre)],

            // An atmospheric beat is empty of threat, not empty of the world:
            // it carries the theme's prop and nothing that can hurt anyone.
            BeatKind.Atmospheric =>
            [
                SpawnProp(
                    objects,
                    map,
                    plan,
                    beat,
                    centre,
                    $"prop.{plan.Theme.ToString().ToLowerInvariant()}-marker",
                    "Weathered Marker"),
            ],
            BeatKind.Interactive =>
            [
                SpawnProp(
                    objects,
                    map,
                    plan,
                    beat,
                    centre,
                    "prop.trestle",
                    "Prop Trestle"),
            ],
            BeatKind.Hazard => [SpawnHazardHint(objects, map, plan, beat)],
            BeatKind.Reward => SpawnReward(objects, map, plan, beat, centre),
            _ => throw new ArgumentOutOfRangeException(nameof(beat)),
        };
    }

    /// <summary>
    /// The hint that makes a hazard fair. It sits on the cell in front of the
    /// hazard band, on the side the player arrives from, and a hazard beat
    /// without one is a bug rather than a variation.
    /// </summary>
    private static ObjectId SpawnHazardHint(
        ObjectStore objects,
        WorldMap map,
        SubzonePlan plan,
        BeatPlan beat)
    {
        var hazard = HazardCells(plan, beat).ToArray();
        if (hazard.Length == 0)
        {
            throw new InvalidOperationException(
                $"Subzone {plan.Index} beat {beat.Index} is a hazard with no " +
                "hazard cells.");
        }

        var (hazardX, _) = hazard[0];
        var hintX = hazardX - 1;
        if (hintX < beat.Cells.XMin)
        {
            throw new InvalidOperationException(
                $"Subzone {plan.Index} beat {beat.Index} has no cell in front " +
                "of its hazard to hint from.");
        }

        return Spawn(
            objects,
            map,
            plan,
            new ObjectSpawn
            {
                TypeId = "decal.cracked-floor",
                Name = "Cracked Floor",
                ShapeId = "loot.generic",
                Location = Anchor(plan, hintX, plan.CorridorY, beat.FloorZ),
                Footprint = new ObjectFootprint(128, 128),
                Height = DecalHeight,

                // A hint is scenery: it must never block the step it warns
                // about, or it would be a wall pretending to be a warning.
                Flags = ObjectFlags.Visible,
            });
    }

    private static IReadOnlyList<ObjectId> SpawnReward(
        ObjectStore objects,
        WorldMap map,
        SubzonePlan plan,
        BeatPlan beat,
        (int X, int Y) centre)
    {
        var chest = Spawn(
            objects,
            map,
            plan,
            new ObjectSpawn
            {
                TypeId = "container.vault-box",
                Name = "Vault Box",
                ShapeId = "container.chest",
                Location = Anchor(plan, centre.X, centre.Y, beat.FloorZ),
                Footprint = new ObjectFootprint(128, 128),
                Height = ChestHeight,
                Flags =
                    ObjectFlags.Container |
                    ObjectFlags.Solid |
                    ObjectFlags.Usable |
                    ObjectFlags.Visible,
                SlotCapacity = 10,
            });

        // Dense goods, so the pack's twelve slots are challenged by weight of
        // value rather than by a spread of singles.
        var gold = objects.Create(new ObjectSpawn
        {
            TypeId = "item.gold",
            Name = "Gold Coins",
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(chest),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Stackable |
                ObjectFlags.Visible,
            Quantity = plan.TreasureRank * 12,
            MaxQuantity = GearSlots.CoinsPerSlot,
            QuantityPerSlot = GearSlots.CoinsPerSlot,
            SlotGroup = GearSlots.CoinGroup,
        });

        var relic = objects.Create(new ObjectSpawn
        {
            TypeId = "item.ash-crown-shard",
            Name = "Ash Crown Shard",
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(chest),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable | ObjectFlags.Visible,
        });

        // This is a second threatened space in five paced beats: forty percent
        // of the subzone contains a static monster, without creating room or
        // encounter-completion state. Either creature may be evaded or lured.
        var guardian = SpawnMonster(
            objects,
            map,
            plan,
            beat,
            plan.GuardianMonsterTypeId,
            (centre.X - 2, centre.Y));

        return [chest, gold, relic, guardian];
    }

    private static ObjectId SpawnMonster(
        ObjectStore objects,
        WorldMap map,
        SubzonePlan plan,
        BeatPlan beat,
        string typeId,
        (int X, int Y) centre)
    {
        var rank = plan.MonsterRank;

        // The archetype is the rank's, and its numbers scale with the rank
        // inside that: a tyrant met early is still a tyrant.
        var authored = MonsterCatalog.TryGet(typeId, out var profile);
        var name = authored
            ? profile.Name
            : typeId == "monster.many-eyed-tyrant"
                ? "Many-Eyed Tyrant"
                : typeId == "monster.goblin-guard"
                    ? "Goblin Guard"
                    : "Cave Rat";
        var body = CombatBalance.GeneratedMonsterBody(typeId, rank);
        var monster = Spawn(
            objects,
            map,
            plan,
            new ObjectSpawn
            {
                TypeId = typeId,
                Name = name,
                ShapeId = typeId == "monster.many-eyed-tyrant" ||
                    authored && profile.Locomotion == MonsterLocomotion.Fly
                    ? "monster.many-eyed"
                    : "monster.goblin",
                Location = Anchor(plan, centre.X, centre.Y, beat.FloorZ),
                Footprint = new ObjectFootprint(128, 128),
                Height = MonsterHeight,
                Flags =
                    ObjectFlags.Actor |
                    ObjectFlags.Monster |
                    ObjectFlags.Container |
                    ObjectFlags.Solid |
                    ObjectFlags.Visible,
                Strength = authored ? profile.Abilities.Strength : 8 + rank,
                Dexterity = authored ? profile.Abilities.Dexterity : 9 + rank,
                Constitution = authored ? profile.Abilities.Constitution : 8 + rank,
                Intelligence = authored ? profile.Abilities.Intelligence : 8,
                Wisdom = authored ? profile.Abilities.Wisdom : 8,
                Charisma = authored ? profile.Abilities.Charisma : 6,
                Class = CharacterClass.Fighter,
                Level = rank,
                Health = body.MaximumConcussion,
                MaxHealth = body.MaximumConcussion,
                Wounds = body.MaximumWounds,
                MaxWounds = body.MaximumWounds,
            });

        var (lootTypeId, lootName) = authored
            ? (profile.LootTypeId, profile.LootName)
            : typeId switch
            {
                "monster.cave-rat" => ("item.rat-tail", "Rat Tail"),
                "monster.goblin-guard" => ("item.copper-token", "Copper Token"),
                _ => ("item.glass-eye", "Glass Eye"),
            };
        objects.Create(new ObjectSpawn
        {
            TypeId = lootTypeId,
            Name = lootName,
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(monster),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Visible,
        });
        return monster;
    }

    private static ObjectId SpawnProp(
        ObjectStore objects,
        WorldMap map,
        SubzonePlan plan,
        BeatPlan beat,
        (int X, int Y) centre,
        string typeId,
        string name) =>
        Spawn(
            objects,
            map,
            plan,
            new ObjectSpawn
            {
                TypeId = typeId,
                Name = name,
                ShapeId = "container.chest",
                Location = Anchor(plan, centre.X, centre.Y, beat.FloorZ),
                Footprint = new ObjectFootprint(128, 128),
                Height = PropHeight,
                Flags =
                    ObjectFlags.Visible |
                    ObjectFlags.Movable |
                    ObjectFlags.Solid |
                    ObjectFlags.AffectedByGravity,
            });

    /// <summary>
    /// Creates one object, but only where the map's placement contract accepts
    /// it. The generator does not get a private route into the world.
    /// </summary>
    private static ObjectId Spawn(
        ObjectStore objects,
        WorldMap map,
        SubzonePlan plan,
        ObjectSpawn spawn)
    {
        var probe = spawn.AsProbe(spawn.Location);
        var placement = map.ValidatePlacement(probe, spawn.Location);
        if (!placement.Allowed)
        {
            throw new InvalidOperationException(
                $"Subzone {plan.Index} cannot place {spawn.Name} at " +
                $"{spawn.Location.Position}: {placement.Message}");
        }

        return objects.Create(spawn);
    }

    private static (int X, int Y) Centre(BeatPlan beat) =>
        ((beat.Cells.XMin + beat.Cells.XMax) / 2,
         (beat.Cells.YMin + beat.Cells.YMax) / 2);

    /// <summary>
    /// A footprint is anchored at the far corner of the cell it occupies, so
    /// the anchor of cell <c>c</c> is <c>(c + 1) * WorldUnitsPerTile</c>.
    /// </summary>
    private static ObjectLocation Anchor(
        SubzonePlan plan,
        int cellX,
        int cellY,
        int z) =>
        ObjectLocation.OnMap(
            plan.MapId,
            new Vec3i(
                checked((cellX + 1) * TileUnits),
                checked((cellY + 1) * TileUnits),
                z));

    private static TerrainCell FloorCell(int floorZ) =>
        new(floorZ, TerrainFlags.Walkable | TerrainFlags.ProvidesSupport);
}

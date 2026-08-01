using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class SubzoneBuilderTests
{
    private static readonly CharacterTier Tier =
        new(Level: 1, CharacterClass.Fighter);

    /// <summary>
    /// The guarantee that matters: whatever the seed rolled, a body that can
    /// climb one Ultima VIII level per step can walk from the first beat to the
    /// last. A subzone whose reward cannot be reached is not a subzone.
    /// </summary>
    [Fact]
    public void EveryBeatIsReachableOnFootFromTheFirst()
    {
        for (ulong seed = 1; seed <= 25; seed++)
        {
            for (var index = 0; index < WorldPlanner.SubzoneCount; index++)
            {
                var plan = WorldPlanner.PlanSubzone(seed, index, Tier);
                var store = new ObjectStore();
                using var build = Dispose(SubzoneBuilder.Build(store, plan));
                var reached = Walk(build.Map, Centre(plan.Beats[0]));

                foreach (var beat in plan.Beats)
                {
                    Assert.True(
                        reached.Contains(Centre(beat)),
                        $"Seed {seed} subzone {index}: beat {beat.Index} " +
                        $"({beat.Kind}) is cut off from the entrance.");
                }
            }
        }
    }

    [Fact]
    public void TerrainIsCarvedOutOfSolidRock()
    {
        var plan = WorldPlanner.PlanSubzone(5, 0, Tier);
        var store = new ObjectStore();
        using var build = Dispose(SubzoneBuilder.Build(store, plan));

        var walkable = SubzoneBuilder.WalkableCells(build.Map).ToArray();
        var total = plan.Width * plan.Depth;

        Assert.NotEmpty(walkable);

        // Rooms and corridors, not a carved-out box: most of the map is rock.
        Assert.True(
            walkable.Length < total / 2,
            $"{walkable.Length} of {total} cells are walkable.");
        Assert.Equal(
            TerrainFlags.Solid,
            build.Map.GetTerrain(0, 0).Flags);
    }

    /// <summary>
    /// A hazard the player cannot see coming is not a hazard, it is a gotcha.
    /// The specification makes the hint mandatory, so the build must produce
    /// one, in front of the danger, and not solid enough to block it.
    /// </summary>
    [Fact]
    public void EveryHazardCarriesAVisibleHintThatDoesNotBlockTheWay()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var plan = WorldPlanner.PlanSubzone(seed, 0, Tier);
            var store = new ObjectStore();
            using var build = Dispose(SubzoneBuilder.Build(store, plan));

            var hazardCells = SubzoneBuilder.WalkableCells(build.Map)
                .Where(cell => build.Map
                    .GetTerrain(cell.X, cell.Y)
                    .Flags
                    .HasFlag(TerrainFlags.Hazard))
                .ToArray();
            Assert.NotEmpty(hazardCells);

            var hint = build.Spawned
                .Select(store.Get)
                .Single(value => value.TypeId == "decal.cracked-floor");
            Assert.True(hint.HasFlag(ObjectFlags.Visible));
            Assert.False(hint.HasFlag(ObjectFlags.Solid));

            // Directly in front of the hazard band, on the approach side.
            var hintCell = CellOf(hint);
            Assert.Contains(
                hazardCells,
                cell => cell.X == hintCell.X + 1 && cell.Y == hintCell.Y);
        }
    }

    [Fact]
    public void TheRewardBeatHoldsGoldScaledToTheTier()
    {
        var plan = WorldPlanner.PlanSubzone(9, 0, Tier with { Level = 4 });
        var store = new ObjectStore();
        using var build = Dispose(SubzoneBuilder.Build(store, plan));

        var chest = build.Spawned
            .Select(store.Get)
            .Single(value => value.TypeId == "container.vault-box");
        var gold = store.GetContents(chest.Id)
            .Select(store.Get)
            .Single(value => value.TypeId == "item.gold");

        Assert.Equal(plan.TreasureRank * 12, gold.Quantity);
        Assert.Equal(GearSlots.CoinsPerSlot, gold.QuantityPerSlot);
        Assert.Equal(GearSlots.CoinGroup, gold.SlotGroup);

        // Dense goods must not blow the pack apart: a purse is one slot.
        Assert.Equal(1, GearSlots.UsedBy([gold]));
    }

    [Fact]
    public void MonsterArchetypeAndNumbersRiseWithTheTier()
    {
        Assert.True(MonsterCatalog.TryGet(MonsterIn(level: 1).TypeId, out _));
        Assert.Equal("monster.goblin-guard", MonsterIn(level: 3).TypeId);
        Assert.Equal("monster.many-eyed-tyrant", MonsterIn(level: 6).TypeId);

        var weak = MonsterIn(level: 1);
        var strong = MonsterIn(level: 6);
        Assert.True(strong.MaxHealth > weak.MaxHealth);
        Assert.Equal(1, weak.Level);
        Assert.Equal(strong.MaxHealth, strong.Health);
    }

    [Fact]
    public void TheSameSeedBuildsTheSameTerrainAndObjects()
    {
        var plan = WorldPlanner.PlanSubzone(1234, 3, Tier);
        var first = new ObjectStore();
        using var left = Dispose(SubzoneBuilder.Build(first, plan));
        var second = new ObjectStore();
        using var right = Dispose(SubzoneBuilder.Build(second, plan));

        Assert.Equal(
            Describe(left.Map, first, left),
            Describe(right.Map, second, right));
    }

    [Fact]
    public void TheBuiltMapKeepsTheStoreInvariants()
    {
        var plan = WorldPlanner.PlanSubzone(77, 2, Tier);
        var store = new ObjectStore();
        using var build = Dispose(SubzoneBuilder.Build(store, plan));

        build.Map.ValidateIndex();
        Assert.Equal(plan.MapId, build.Map.MapId);

        // Treasure spawns inside its chest, so the map indexes what the builder
        // put on the ground and nothing else.
        Assert.Equal(
            build.Spawned
                .Select(store.Get)
                .Count(value => value.Location.Kind == LocationKind.OnMap),
            build.Map.IndexedObjectCount);

        // Everything the builder placed sits on a floor it did not sink into.
        foreach (var value in build.Map.QueryAll())
        {
            var (x, y) = WorldMap.SupportPointOf(value);
            var cell = build.Map.GetTerrain(
                x / WorldMap.WorldUnitsPerTile,
                y / WorldMap.WorldUnitsPerTile);
            Assert.True(cell.Flags.HasFlag(TerrainFlags.Walkable));
            Assert.Equal(cell.FloorZ, value.Location.Position.Z);
        }
    }

    /// <summary>
    /// Two subzones on one store are two maps, which is the multi-map shape the
    /// eighteen-subzone world needs.
    /// </summary>
    [Fact]
    public void SubzonesShareAStoreWithoutSharingAMap()
    {
        var store = new ObjectStore();
        using var first = Dispose(
            SubzoneBuilder.Build(store, WorldPlanner.PlanSubzone(8, 0, Tier)));
        using var second = Dispose(
            SubzoneBuilder.Build(store, WorldPlanner.PlanSubzone(8, 1, Tier)));

        Assert.Equal(2, store.Maps.All.Count);
        Assert.NotEqual(first.Map.MapId, second.Map.MapId);
        Assert.Empty(
            first.Spawned.Intersect(second.Spawned));
        first.Map.ValidateIndex();
        second.Map.ValidateIndex();
    }

    private static WorldObject MonsterIn(int level)
    {
        var plan = WorldPlanner.PlanSubzone(3, 0, Tier with { Level = level });
        var store = new ObjectStore();
        using var build = Dispose(SubzoneBuilder.Build(store, plan));
        return build.Spawned
            .Select(store.Get)
            .First(value => value.HasFlag(ObjectFlags.Monster));
    }

    /// <summary>
    /// Every cell reachable from <paramref name="from"/> by cardinal steps that
    /// climb no more than the Avatar clears without a <see cref="VaultCheck"/>.
    /// </summary>
    private static HashSet<(int X, int Y)> Walk(
        WorldMap map,
        (int X, int Y) from)
    {
        var seen = new HashSet<(int X, int Y)> { from };
        var pending = new Queue<(int X, int Y)>();
        pending.Enqueue(from);
        while (pending.Count > 0)
        {
            var (x, y) = pending.Dequeue();
            var here = map.GetTerrain(x, y);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (X: x + dx, Y: y + dy);
                if (next.X < 0 || next.X >= map.Width ||
                    next.Y < 0 || next.Y >= map.Depth ||
                    seen.Contains(next))
                {
                    continue;
                }

                var cell = map.GetTerrain(next.X, next.Y);
                if (!cell.Flags.HasFlag(TerrainFlags.Walkable) ||
                    cell.FloorZ - here.FloorZ >
                        PlayableSliceWorld.StepHeightUnits)
                {
                    continue;
                }

                seen.Add(next);
                pending.Enqueue(next);
            }
        }

        return seen;
    }

    private static (int X, int Y) Centre(BeatPlan beat) =>
        ((beat.Cells.XMin + beat.Cells.XMax) / 2,
         (beat.Cells.YMin + beat.Cells.YMax) / 2);

    private static (int X, int Y) CellOf(WorldObject value) =>
        ((value.Location.Position.X / WorldMap.WorldUnitsPerTile) - 1,
         (value.Location.Position.Y / WorldMap.WorldUnitsPerTile) - 1);

    private static string Describe(
        WorldMap map,
        ObjectStore store,
        BuildScope build)
    {
        var terrain = new List<string>();
        for (var y = 0; y < map.Depth; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var cell = map.GetTerrain(x, y);
                terrain.Add($"{x},{y}={cell.FloorZ}/{cell.Flags}");
            }
        }

        return string.Join("\n", terrain) + "\n" + string.Join(
            "\n",
            build.Spawned
                .Select(store.Get)
                .Select(value =>
                    $"{value.TypeId} {Where(value.Location)} " +
                    $"{value.Quantity} {value.MaxHealth}"));
    }

    private static string Where(ObjectLocation location) =>
        location.Kind == LocationKind.OnMap
            ? location.Position.ToString()
            : location.Kind.ToString();

    /// <summary>
    /// Disposes the map a build owns when the test is done with it, so the next
    /// build in the same test does not inherit a registered map.
    /// </summary>
    private static BuildScope Dispose(SubzoneBuild build) => new(build);

    private sealed class BuildScope(SubzoneBuild build) : IDisposable
    {
        public WorldMap Map => build.Map;

        public IReadOnlyList<ObjectId> Spawned => build.Spawned;

        public void Dispose() => build.Map.Dispose();
    }
}

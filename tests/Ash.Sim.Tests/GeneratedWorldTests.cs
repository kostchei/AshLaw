using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

/// <summary>
/// The first vertical slice as something a player actually completes: one
/// five-beat subzone, a guarded reward, advancement, and a persistent save.
/// Beats are generation pacing, never runtime room objectives.
/// </summary>
public sealed class GeneratedWorldTests
{
    private const ulong Seed = 987654321;
    private const int MaxWalkSteps = 600;

    [Fact]
    public void AFreshWorldBuildsOnlyTheFirstSubzone()
    {
        using var world = PlayableSliceWorld.CreateGenerated(Seed);

        Assert.Single(world.Objects.Maps.All);
        Assert.Equal(1, world.CurrentMapId);
        Assert.Equal(WorldPlanner.MapWidth, world.CurrentMap.Width);
        Assert.Equal(2, world.Character.Talents.Count);

        var here = world.PlayerPosition;
        Assert.True(
            world.CurrentMap.GetTerrain(here.X, here.Y).Flags
                .HasFlag(TerrainFlags.Walkable));
        Assert.Contains(world.BackpackItems, item => item.Name == "Apple");
        Assert.Contains(world.BackpackItems, item => item.Name == "Rusty Sword");

        world.Objects.ValidateInvariants();
        world.CurrentMap.ValidateIndex();
    }

    [Fact]
    public void TheSingleSubzoneHasOnlyItsVictoryExit()
    {
        using var world = PlayableSliceWorld.CreateGenerated(Seed);

        Assert.Single(world.Objects.Maps.All);
        Assert.Equal(
            [SubzoneBuilder.ExitTypeId],
            WaymarkTypesOn(world.CurrentMap));
    }

    [Fact]
    public void AConfirmedRogueSuppliesScoresClassTalentsAndStartingGear()
    {
        var dice = new Dice(424242);
        var rolled = CharacterCreation.RollUnearthedArcana(
            RulesRepository.CharacterCreation,
            dice,
            CharacterClass.Rogue,
            Ancestry.Human);
        var character = CharacterCreation.ResolveTalentsWithFirstLegalChoices(
            CharacterCreation.RollTalents(
                RulesRepository.CharacterCreation,
                dice,
                rolled));

        using var world = PlayableSliceWorld.CreateGenerated(Seed, character);

        Assert.Equal(character.Scores, world.Player.Abilities);
        Assert.Equal(CharacterClass.Rogue, world.Player.Class);
        Assert.Equal(character, world.Character);
        Assert.Equal(2, world.Character.Talents.Count);
        Assert.Contains(world.BackpackItems, item => item.Name == "Bronze Dagger");
        Assert.DoesNotContain(world.BackpackItems, item => item.Name == "Wooden Shield");
    }

    [Fact]
    public void GeneratedFeaturesAreContextualObjectsRatherThanRoomState()
    {
        using var markerWorld = PlayableSliceWorld.CreateGenerated(Seed);
        var marker = markerWorld.CurrentMap.QueryAll()
            .Single(value => value.TypeId.EndsWith("-marker", StringComparison.Ordinal));
        PlacePlayerBeside(markerWorld, marker);

        var inspected = markerWorld.Interact();

        Assert.True(inspected.Succeeded, inspected.Message);
        Assert.Contains("unstable ground", inspected.Message, StringComparison.Ordinal);

        using var trestleWorld = PlayableSliceWorld.CreateGenerated(Seed);
        var trestle = trestleWorld.CurrentMap.QueryAll()
            .Single(value => value.TypeId == "prop.trestle");
        PlacePlayerBeside(trestleWorld, trestle);

        var dismantled = trestleWorld.Interact();

        Assert.True(dismantled.Succeeded, dismantled.Message);
        Assert.False(trestleWorld.Objects.TryGet(trestle.Id, out _));
    }

    [Fact]
    public void CrossingGeneratedCrackedFloorResolvesARealHazard()
    {
        using var world = PlayableSliceWorld.CreateGenerated(Seed);
        var hint = world.CurrentMap.QueryAll()
            .Single(value => value.TypeId == "decal.cracked-floor");
        PlacePlayerAt(world, hint.Location.Position);
        var diceBefore = world.Dice.State;

        var crossed = world.MovePlayer(1, 0);

        Assert.True(crossed.Succeeded, crossed.Message);
        Assert.True(world.CurrentMap
            .GetTerrain(world.PlayerPosition.X, world.PlayerPosition.Y)
            .Flags
            .HasFlag(TerrainFlags.Hazard));
        Assert.StartsWith("Hazard:", crossed.Message, StringComparison.Ordinal);
        Assert.NotEqual(diceBefore, world.Dice.State);
        Assert.InRange(world.Player.Health, 0, world.Player.MaxHealth);
    }

    [Fact]
    public void GeneratedMonsterLootBecomesLootableOnItsCorpse()
    {
        using var world = PlayableSliceWorld.CreateGenerated(Seed);
        var monster = world.Monsters.First();
        var carried = Assert.Single(world.ContentsOf(monster.Id));

        _ = world.Trauma.Apply(
            world.PlayerId,
            monster.Id,
            [new TraumaEffect(TraumaEffectKind.Death)]);

        var corpse = world.Objects.Get(monster.Id);
        Assert.True(corpse.HasFlag(ObjectFlags.Corpse));
        Assert.False(corpse.HasFlag(ObjectFlags.Monster));
        Assert.Equal(carried.Id, Assert.Single(world.ContentsOf(corpse.Id)).Id);
    }

    /// <summary>
    /// Treasure gates the exit, which advances exactly once. Living monsters
    /// do not: combat remains one possible solution rather than an XP task.
    /// Save/load retains the rolled character and completed dungeon.
    /// </summary>
    [Fact]
    public void CompletingTheFirstDungeonAdvancesAndSurvivesSaveLoad()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ash-generated-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var world = PlayableSliceWorld.CreateGenerated(Seed);
        try
        {
            var path = Path.Combine(directory, "world.ashw");
            var exit = world.Waymarks.Single(mark =>
                mark.TypeId == SubzoneBuilder.ExitTypeId);
            WalkTo(world, world.GetGridPosition(exit.Id));
            Assert.Equal(world.GetGridPosition(exit.Id), world.PlayerPosition);

            var blockedByReward = world.UseNearestWaymark();
            Assert.False(blockedByReward.Succeeded);
            Assert.Contains("reward", blockedByReward.Message);
            Assert.Equal(2, world.Monsters.Count);

            var vault = world.Objects.Enumerate().Single(value =>
                value.TypeId == "container.vault-box");
            var rewards = world.ContentsOf(vault.Id);
            var claimed = world.Transfers.Execute(
                rewards.Select(reward => new ObjectTransferRequest(
                    reward.Id,
                    reward.Location,
                    ObjectLocation.InContainer(world.PlayerId))).ToArray());
            Assert.True(claimed.Succeeded, claimed.Message);

            var before = world.Player;
            var completed = world.UseNearestWaymark();
            Assert.True(completed.Succeeded, completed.Message);
            Assert.True(world.DungeonCompleted);
            Assert.Equal(2, world.Monsters.Count);
            Assert.Equal(PlayableSliceWorld.FirstDungeonExperience, world.Experience);
            Assert.Equal(1, world.Player.Level);
            Assert.True(world.AdvancementChoicePending);
            Assert.NotNull(world.AdvancementTalent);

            Assert.True(world.RequestSave(path).Succeeded);
            using (var pending = PlayableSliceWorld.Load(path))
            {
                Assert.Equal(1, pending.Player.Level);
                Assert.True(pending.DungeonCompleted);
                Assert.True(pending.AdvancementChoicePending);
                Assert.Equal(
                    world.AdvancementTalent,
                    pending.AdvancementTalent);
                Assert.NotEmpty(pending.LegalAdvancementChoices);
                Assert.Equal(2, pending.Monsters.Count);
                var loadedBoss = Assert.Single(pending.Monsters.Where(monster =>
                    MonsterCatalog.Get(monster.TypeId).Mutation is not null));
                var loadedBossProfile = MonsterCatalog.Get(loadedBoss.TypeId);
                Assert.Equal(3, loadedBossProfile.TreasureLevel);
                Assert.Contains(
                    ".mutation.",
                    loadedBoss.TypeId,
                    StringComparison.Ordinal);
                Assert.All(pending.Monsters, monster =>
                    Assert.Equal(
                        MonsterCatalog.Get(monster.TypeId).AttackBonus,
                        pending.Sheets.For(monster.Id).AttackModifier));
            }

            var advanced = world.ResolveAdvancementTalent(
                world.LegalAdvancementChoices[0]);
            Assert.True(advanced.Succeeded, advanced.Message);
            Assert.Equal(2, world.Player.Level);
            Assert.True(world.Player.MaxHealth > before.MaxHealth);
            Assert.NotNull(world.AdvancementTalent!.Choice);

            var savedPlayer = world.Player;
            var savedDice = world.Dice.State;
            var savedTick = world.Physics.Tick;
            var savedTerrain = Describe(world.CurrentMap);

            Assert.True(world.RequestSave(path).Succeeded);
            using var loaded = PlayableSliceWorld.Load(path);

            Assert.Equal(1, loaded.CurrentMapId);
            Assert.Equal(savedPlayer, loaded.Player);
            Assert.Equal(savedTick, loaded.Physics.Tick);
            Assert.Equal(savedDice, loaded.Dice.State);
            Assert.Single(loaded.Objects.Maps.All);
            Assert.Equal(savedTerrain, Describe(loaded.CurrentMap));
            Assert.True(loaded.DungeonCompleted);
            Assert.Equal(world.Experience, loaded.Experience);
            Assert.Equal(world.Character, loaded.Character);
            Assert.Equal(world.AdvancementTalent, loaded.AdvancementTalent);

            loaded.Objects.ValidateInvariants();
            loaded.Physics.ValidateInvariants();
            loaded.CurrentMap.ValidateIndex();

            var repeated = loaded.UseNearestWaymark();
            Assert.True(repeated.Succeeded, repeated.Message);
            Assert.Equal(PlayableSliceWorld.FirstDungeonExperience, loaded.Experience);
            Assert.Equal(2, loaded.Player.Level);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string[] WaymarkTypesOn(WorldMap map) =>
        map.QueryAll()
            .Select(value => value.TypeId)
            .Where(typeId =>
                typeId == SubzoneBuilder.EntranceTypeId ||
                typeId == SubzoneBuilder.ExitTypeId)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string Describe(WorldMap map)
    {
        var cells = new List<string>(map.Width * map.Depth);
        for (var y = 0; y < map.Depth; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var cell = map.GetTerrain(x, y);
                cells.Add($"{cell.FloorZ}/{cell.Flags}");
            }
        }

        return string.Join(",", cells);
    }

    private static void PlacePlayerBeside(
        PlayableSliceWorld world,
        WorldObject feature) =>
        PlacePlayerAt(
            world,
            feature.Location.Position with
            {
                X = feature.Location.Position.X - PlayableSliceWorld.WorldUnitsPerTile,
            });

    private static void PlacePlayerAt(PlayableSliceWorld world, Vec3i position)
    {
        var placed = world.Transfers.Execute(
            new ObjectTransferRequest(
                world.PlayerId,
                world.Player.Location,
                ObjectLocation.OnMap(world.CurrentMapId, position)));
        Assert.True(placed.Succeeded, placed.Message);
    }

    private static void WalkTo(PlayableSliceWorld world, GridPosition target)
    {
        var blocked = new HashSet<GridPosition>();
        var stepsTaken = 0;
        while (world.PlayerPosition != target)
        {
            var route = FindRoute(
                world.CurrentMap,
                world.PlayerPosition,
                target,
                blocked);
            if (route is null)
            {
                throw new InvalidOperationException(
                    $"No walkable route from {world.PlayerPosition} to " +
                    $"{target} on map {world.CurrentMapId}.");
            }

            foreach (var next in route)
            {
                if (++stepsTaken > MaxWalkSteps)
                {
                    throw new InvalidOperationException(
                        $"Took {MaxWalkSteps} steps without reaching {target}.");
                }

                var here = world.PlayerPosition;
                var move = CombatRound.Step(
                    world,
                    next.X - here.X,
                    next.Y - here.Y);
                if (!move.Succeeded)
                {
                    blocked.Add(next);
                    break;
                }
            }
        }
    }

    private static IReadOnlyList<GridPosition>? FindRoute(
        WorldMap map,
        GridPosition from,
        GridPosition to,
        IReadOnlySet<GridPosition> blocked)
    {
        var cameFrom = new Dictionary<GridPosition, GridPosition> { [from] = from };
        var pending = new Queue<GridPosition>();
        pending.Enqueue(from);
        while (pending.Count > 0)
        {
            var here = pending.Dequeue();
            if (here == to)
            {
                return Retrace(cameFrom, from, to);
            }

            var floorZ = map.GetTerrain(here.X, here.Y).FloorZ;
            foreach (var (deltaX, deltaY) in
                     new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = here.Offset(deltaX, deltaY);
                if (next.X < 0 || next.X >= map.Width ||
                    next.Y < 0 || next.Y >= map.Depth ||
                    blocked.Contains(next) ||
                    cameFrom.ContainsKey(next))
                {
                    continue;
                }

                var cell = map.GetTerrain(next.X, next.Y);
                if (!cell.Flags.HasFlag(TerrainFlags.Walkable) ||
                    cell.FloorZ - floorZ > PlayableSliceWorld.StepHeightUnits)
                {
                    continue;
                }

                cameFrom[next] = here;
                pending.Enqueue(next);
            }
        }

        return null;
    }

    private static IReadOnlyList<GridPosition> Retrace(
        IReadOnlyDictionary<GridPosition, GridPosition> cameFrom,
        GridPosition from,
        GridPosition to)
    {
        var route = new List<GridPosition>();
        for (var cell = to; cell != from; cell = cameFrom[cell])
        {
            route.Add(cell);
        }

        route.Reverse();
        return route;
    }
}

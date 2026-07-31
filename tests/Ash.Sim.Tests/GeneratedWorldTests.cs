using Ash.Rules;

namespace Ash.Sim.Tests;

/// <summary>
/// The generated world as something a player actually moves through: eighteen
/// built subzones, a way on at the end of each, and a save that comes back on
/// the subzone it was taken in.
/// </summary>
public sealed class GeneratedWorldTests
{
    /// <summary>
    /// A seed with no special properties. It is fixed only so a failure is the
    /// same failure twice.
    /// </summary>
    private const ulong Seed = 987654321;

    private const int MaxWalkSteps = 600;

    [Fact]
    public void AFreshWorldBuildsEverySubzoneAndStartsInTheFirst()
    {
        using var world = PlayableSliceWorld.CreateGenerated(Seed);

        Assert.Equal(
            WorldPlanner.SubzoneCount,
            world.Objects.Maps.All.Count);
        Assert.Equal(1, world.CurrentMapId);
        Assert.Equal(WorldPlanner.MapWidth, world.CurrentMap.Width);

        // The Avatar stands on carved floor, not in the rock every subzone is
        // cut out of, and carries what he was given.
        var here = world.PlayerPosition;
        Assert.True(
            world.CurrentMap
                .GetTerrain(here.X, here.Y)
                .Flags
                .HasFlag(TerrainFlags.Walkable));
        Assert.Contains(world.BackpackItems, item => item.Name == "Apple");
        Assert.Contains(
            world.BackpackItems,
            item => item.Name == "Rusty Sword");

        world.Objects.ValidateInvariants();
        foreach (var map in world.Objects.Maps.All)
        {
            map.ValidateIndex();
        }
    }

    /// <summary>
    /// The first subzone has nowhere to go back to and the last has nowhere to
    /// go on to, so neither carries a mark that leads out of the world.
    /// </summary>
    [Fact]
    public void TheEndsOfTheWorldHaveOnlyTheWaysThatLeadSomewhere()
    {
        using var world = PlayableSliceWorld.CreateGenerated(Seed);
        var maps = world.Objects.Maps.All;

        Assert.Equal(
            [SubzoneBuilder.ExitTypeId],
            WaymarkTypesOn(maps[0]));
        Assert.Equal(
            [SubzoneBuilder.EntranceTypeId, SubzoneBuilder.ExitTypeId],
            WaymarkTypesOn(maps[1]));
        Assert.Equal(
            [SubzoneBuilder.EntranceTypeId],
            WaymarkTypesOn(maps[^1]));
    }

    /// <summary>
    /// The whole loop the slice exists to prove: start in the first generated
    /// subzone, walk to its way on, step through into the second, save there,
    /// and load back into the second subzone with the same Avatar, the same
    /// pack, the same terrain and the same dice.
    /// </summary>
    [Fact]
    public void WalkingThroughToTheNextSubzoneSurvivesASaveAndALoad()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ash-generated-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        using var world = PlayableSliceWorld.CreateGenerated(Seed);
        try
        {
            var path = Path.Combine(directory, "world.ashw");

            // Draw the sword, so the walk carries something worn as well as
            // something packed.
            Assert.True(world.ToggleRightHand().Succeeded);

            var exit = world.Waymarks.Single(mark =>
                mark.TypeId == SubzoneBuilder.ExitTypeId);
            WalkTo(world, world.GetGridPosition(exit.Id));

            // Nothing has been transferred to another map yet: the way on is a
            // thing in the world, and until it is used it is only scenery.
            Assert.Equal(1, world.CurrentMapId);

            var travelled = world.Interact();

            Assert.True(travelled.Succeeded, travelled.Message);
            Assert.Equal(2, world.CurrentMapId);
            Assert.Equal(2, world.Player.Location.MapId);
            Assert.Equal(MotionState.Resting, world.Player.Motion);

            // He comes out at the second subzone's way in, standing on it.
            var arrival = world.Waymarks.Single(mark =>
                mark.TypeId == SubzoneBuilder.EntranceTypeId);
            Assert.Equal(
                world.GetGridPosition(arrival.Id),
                world.PlayerPosition);

            // The pack travelled because its owner did.
            Assert.Contains(world.BackpackItems, item => item.Name == "Apple");
            Assert.Equal(
                "Rusty Sword",
                world.EquippedIn(EquipmentSlot.RightHand)?.Name);

            var savedPlayer = world.Player;
            var savedDice = world.Dice.State;
            var savedTick = world.Physics.Tick;
            var savedTerrain = Describe(world.CurrentMap);
            var savedLeftBehind = Describe(world.Objects.Maps.Get(1));

            Assert.True(world.RequestSave(path).Succeeded);
            using var loaded = PlayableSliceWorld.Load(path);

            // Identity, and the subzone he was standing in.
            Assert.Equal(2, loaded.CurrentMapId);
            Assert.Equal(savedPlayer, loaded.Player);
            Assert.Equal(savedTick, loaded.Physics.Tick);

            // Dice state, so the loaded world rolls what this one would have.
            Assert.Equal(savedDice, loaded.Dice.State);

            // Inventory.
            Assert.Contains(loaded.BackpackItems, item => item.Name == "Apple");
            Assert.Equal(
                "Rusty Sword",
                loaded.EquippedIn(EquipmentSlot.RightHand)?.Name);

            // Terrain — of the subzone he is in, and of the one he left, which
            // a world that only saved the current map would have lost.
            Assert.Equal(
                WorldPlanner.SubzoneCount,
                loaded.Objects.Maps.All.Count);
            Assert.Equal(savedTerrain, Describe(loaded.CurrentMap));
            Assert.Equal(
                savedLeftBehind,
                Describe(loaded.Objects.Maps.Get(1)));

            loaded.Objects.ValidateInvariants();
            loaded.Physics.ValidateInvariants();
            foreach (var map in loaded.Objects.Maps.All)
            {
                map.ValidateIndex();
            }

            // And the way back out of the second subzone still works from the
            // loaded world, which is the whole point of putting both ends in
            // the world rather than in a plan nothing saves.
            var back = loaded.Interact();
            Assert.True(back.Succeeded, back.Message);
            Assert.Equal(1, loaded.CurrentMapId);
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

    /// <summary>
    /// Walks the Avatar to <paramref name="target"/> through the same
    /// <see cref="PlayableSliceWorld.MovePlayer"/> a player's keypress uses,
    /// one cardinal step at a time and letting the round's move refill.
    /// </summary>
    /// <remarks>
    /// The route is planned over terrain alone, because terrain is what the
    /// generator guarantees is connected. Objects are not planned around: a
    /// refused step marks that cell and the route is planned again without it,
    /// which is how the guardian standing in the middle of the first room gets
    /// walked round rather than through.
    /// </remarks>
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
                        $"Took {MaxWalkSteps} steps without reaching " +
                        $"{target}.");
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

    /// <summary>
    /// The cells of a shortest walk from <paramref name="from"/> to
    /// <paramref name="to"/>, climbing no more per step than the Avatar clears
    /// without a <see cref="VaultCheck"/>. Null when there is no such walk.
    /// </summary>
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

using Ash.Core;

namespace Ash.Sim.Tests;

/// <summary>
/// The pathfinder is asserted against the map's own contracts, never against a
/// second walkability model: every fixture here changes terrain or places an
/// ordinary solid object, and the search is expected to notice.
/// </summary>
public sealed class SpatialPathfinderTests
{
    private const int Tile = WorldMap.WorldUnitsPerTile;

    [Fact]
    public void AnOpenFloorGivesTheShortestFourConnectedRoute()
    {
        var store = new ObjectStore();
        var walker = SpawnWalker(store, tileX: 1, tileY: 1);
        using var map = new WorldMap(store, 0, width: 10, depth: 10);
        var pathfinder = new SpatialPathfinder(store);

        var route = pathfinder.FindPath(walker, Anchor(4, 3));

        Assert.True(route.Found, route.Message);
        Assert.Equal(5, route.Steps.Count);
        Assert.Equal(Anchor(4, 3), route.Steps[^1]);
        Assert.All(
            route.Steps.Prepend(store.Get(walker).Location.Position)
                .Zip(route.Steps),
            pair => Assert.Equal(
                Tile,
                Math.Abs(pair.First.X - pair.Second.X) +
                Math.Abs(pair.First.Y - pair.Second.Y)));
    }

    [Fact]
    public void ASolidObjectAcrossTheDirectLineIsRoutedAround()
    {
        var store = new ObjectStore();
        var walker = SpawnWalker(store, tileX: 1, tileY: 3);
        for (var tileY = 0; tileY <= 5; tileY++)
        {
            if (tileY == 5)
            {
                continue;
            }

            SpawnWall(store, tileX: 3, tileY: tileY);
        }

        using var map = new WorldMap(store, 0, width: 10, depth: 10);
        var pathfinder = new SpatialPathfinder(store);

        var route = pathfinder.FindPath(walker, Anchor(5, 3));

        // The only gap in the wall is at y = 5, so a route that exists at all
        // must go through it. A parallel nav grid would have to be told about
        // these objects; the spatial index already knows.
        Assert.True(route.Found, route.Message);
        Assert.Contains(route.Steps, step => step == Anchor(3, 5));
        Assert.DoesNotContain(
            route.Steps,
            step => step.X == Anchor(3, 0).X && step.Y <= Anchor(3, 4).Y);
        Assert.Equal(Anchor(5, 3), route.Steps[^1]);
    }

    [Fact]
    public void AWalledInMoverIsRefusedRatherThanSearchingForever()
    {
        var store = new ObjectStore();
        var walker = SpawnWalker(store, tileX: 2, tileY: 2);
        foreach (var (tileX, tileY) in new[]
                 {
                     (1, 2), (3, 2), (2, 1), (2, 3),
                 })
        {
            SpawnWall(store, tileX, tileY);
        }

        using var map = new WorldMap(store, 0, width: 10, depth: 10);
        var pathfinder = new SpatialPathfinder(store);

        var route = pathfinder.FindPath(walker, Anchor(8, 8));

        Assert.False(route.Found);
        Assert.Equal(PathFailure.Unreachable, route.Failure);
        Assert.Empty(route.Steps);
    }

    [Fact]
    public void SolidTerrainBlocksARouteExactlyAsAPlacementWouldBeRefused()
    {
        var store = new ObjectStore();
        var walker = SpawnWalker(store, tileX: 1, tileY: 1);
        using var map = new WorldMap(store, 0, width: 6, depth: 6);
        for (var tileY = 0; tileY < 6; tileY++)
        {
            map.SetTerrain(3, tileY, new TerrainCell(0, TerrainFlags.Solid));
        }

        var pathfinder = new SpatialPathfinder(store);

        Assert.False(pathfinder.FindPath(walker, Anchor(5, 1)).Found);
        Assert.True(pathfinder.FindPath(walker, Anchor(2, 4)).Found);
    }

    [Fact]
    public void AnArrivalRadiusLetsAPursuerStopBesideAnOccupiedGoal()
    {
        var store = new ObjectStore();
        var walker = SpawnWalker(store, tileX: 1, tileY: 1);
        var quarry = SpawnWalker(store, tileX: 5, tileY: 1);
        using var map = new WorldMap(store, 0, width: 10, depth: 10);
        var pathfinder = new SpatialPathfinder(store);
        var goal = store.Get(quarry).Location.Position;

        // The goal tile is a solid body, so it can never validate as a
        // placement. Asking for adjacency is how a pursuit says what it means.
        Assert.False(pathfinder.FindPath(walker, goal).Found);

        var route = pathfinder.FindPath(walker, goal, arrivalRadiusTiles: 1);

        Assert.True(route.Found, route.Message);
        Assert.Equal(Anchor(4, 1), route.Steps[^1]);
    }

    [Fact]
    public void TheSameWorldAlwaysProducesTheSameRoute()
    {
        static PathResult Route()
        {
            var store = new ObjectStore();
            var walker = SpawnWalker(store, tileX: 1, tileY: 1);
            SpawnWall(store, tileX: 2, tileY: 1);
            SpawnWall(store, tileX: 1, tileY: 2);
            using var map = new WorldMap(store, 0, width: 8, depth: 8);
            return new SpatialPathfinder(store).FindPath(walker, Anchor(4, 4));
        }

        var first = Route();
        var second = Route();

        Assert.True(first.Found, first.Message);
        Assert.Equal(first.Steps, second.Steps);
        Assert.Equal(first.NodesExpanded, second.NodesExpanded);
    }

    [Fact]
    public void ANodeBudgetBoundsTheSearchInsteadOfStallingABeat()
    {
        var store = new ObjectStore();
        var walker = SpawnWalker(store, tileX: 1, tileY: 1);
        for (var tileY = 0; tileY < 40; tileY++)
        {
            SpawnWall(store, tileX: 20, tileY: tileY);
        }

        using var map = new WorldMap(store, 0, width: 40, depth: 40);
        var pathfinder = new SpatialPathfinder(store);

        var route = pathfinder.FindPath(walker, Anchor(38, 38), nodeBudget: 8);

        Assert.False(route.Found);
        Assert.Equal(PathFailure.BudgetExhausted, route.Failure);
        Assert.True(route.NodesExpanded <= 9);
    }

    /// <summary>The anchor of a tile: the far corner of the cell it occupies.</summary>
    private static Vec3i Anchor(int tileX, int tileY) =>
        new((tileX + 1) * Tile, (tileY + 1) * Tile, 0);

    private static ObjectId SpawnWalker(ObjectStore store, int tileX, int tileY) =>
        store.Create(new ObjectSpawn
        {
            TypeId = "test.walker",
            Name = "Walker",
            ShapeId = "actor",
            Location = ObjectLocation.OnMap(0, Anchor(tileX, tileY)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 32,
            StepHeight = 8,
            Flags = ObjectFlags.Actor | ObjectFlags.Solid | ObjectFlags.Visible,
        });

    private static ObjectId SpawnWall(ObjectStore store, int tileX, int tileY) =>
        store.Create(new ObjectSpawn
        {
            TypeId = "test.wall",
            Name = "Wall",
            ShapeId = "prop",
            Location = ObjectLocation.OnMap(0, Anchor(tileX, tileY)),
            Footprint = new ObjectFootprint(256, 256),
            Height = 64,
            Flags = ObjectFlags.Solid | ObjectFlags.Fixed | ObjectFlags.Visible,
        });
}

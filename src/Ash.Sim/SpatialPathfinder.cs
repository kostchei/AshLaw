using Ash.Core;

namespace Ash.Sim;

public enum PathFailure : byte
{
    None = 0,

    /// <summary>The mover is not standing on a map, so there is nothing to route through.</summary>
    NotOnMap = 1,

    /// <summary>Every reachable tile was expanded and none reached the goal.</summary>
    Unreachable = 2,

    /// <summary>The search hit its node budget before reaching the goal.</summary>
    BudgetExhausted = 3,
}

/// <summary>
/// A route as the anchors it passes through, in order, excluding where the
/// mover already stands.
/// </summary>
/// <remarks>
/// Anchors rather than tile coordinates, because the elevation of each step is
/// part of the answer: the search resolves the surface a step lands on through
/// the same support query movement uses, so a caller committing
/// <see cref="Steps"/> one at a time never has to guess a Z.
/// </remarks>
public sealed record PathResult(
    bool Found,
    IReadOnlyList<Vec3i> Steps,
    int NodesExpanded,
    PathFailure Failure,
    string Message)
{
    public static PathResult Refused(PathFailure failure, int expanded, string message) =>
        new(false, [], expanded, failure, message);
}

/// <summary>
/// Obstacle-routing A* over the map's own terrain and spatial index.
/// </summary>
/// <remarks>
/// There is deliberately no navigation grid. A parallel walkability structure is
/// a second answer to "can this body stand here", and the moment content moves a
/// crate the two answers disagree — the drift spec §20 names. So every candidate
/// tile is decided by the same two calls a real step makes:
/// <see cref="WorldMap.FindSupport"/> for the surface it would land on, and
/// <see cref="WorldMap.ValidatePlacement"/> for whether the mover's own footprint
/// fits there. That makes the search cost real, which is what the node budget is
/// for: a search that cannot answer cheaply says so instead of stalling a beat.
///
/// The lattice is the four cardinal tiles, matching what a step commits; the
/// heuristic is tile-Manhattan, which never overestimates a four-neighbour walk,
/// so the first route found is a shortest one. Ties break on a total order of
/// coordinates, so two runs of the same world produce the same route.
/// </remarks>
public sealed class SpatialPathfinder
{
    /// <summary>
    /// How many tiles one search may expand. Roughly a 45-tile radius of open
    /// floor: far more than a pursuit needs, and small enough that a monster
    /// walled into a closed room fails on the beat it asks rather than walking
    /// the whole map.
    /// </summary>
    public const int DefaultNodeBudget = 2048;

    private static readonly (int X, int Y)[] Neighbours =
    [
        (0, -1),
        (1, 0),
        (0, 1),
        (-1, 0),
    ];

    private readonly ObjectStore _objects;

    public SpatialPathfinder(ObjectStore objects)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
    }

    /// <summary>
    /// A route for <paramref name="moverId"/> to within
    /// <paramref name="arrivalRadiusTiles"/> of <paramref name="goal"/>.
    /// </summary>
    /// <remarks>
    /// The arrival radius exists because the interesting goal is usually
    /// occupied — a pursuer wants the tile beside its quarry, not the tile the
    /// quarry is standing in, and that tile can never validate as placement.
    /// Asking for adjacency states that directly instead of special-casing the
    /// destination out of the collision rules.
    /// </remarks>
    public PathResult FindPath(
        ObjectId moverId,
        Vec3i goal,
        int arrivalRadiusTiles = 0,
        int nodeBudget = DefaultNodeBudget)
    {
        if (arrivalRadiusTiles < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrivalRadiusTiles));
        }

        if (nodeBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nodeBudget));
        }

        var mover = _objects.Get(moverId);
        if (mover.Location.Kind != LocationKind.OnMap)
        {
            return PathResult.Refused(
                PathFailure.NotOnMap,
                0,
                $"{mover.Name} is not on a map and cannot be routed.");
        }

        var map = _objects.Maps.Get(mover.Location.MapId);
        var start = mover.Location.Position;
        var startTile = TileOf(start);
        var goalTile = TileOf(goal);
        if (TileDistance(startTile, goalTile) <= arrivalRadiusTiles)
        {
            return new PathResult(
                true,
                [],
                0,
                PathFailure.None,
                $"{mover.Name} already stands within reach of its destination.");
        }

        var reach = Math.Max(0, mover.StepHeight);
        var cameFrom = new Dictionary<(int X, int Y), (int X, int Y)>();
        var anchors = new Dictionary<(int X, int Y), Vec3i> { [startTile] = start };
        var best = new Dictionary<(int X, int Y), int> { [startTile] = 0 };
        var open = new PriorityQueue<(int X, int Y), (int F, int H, int X, int Y)>();
        var startHeuristic = TileDistance(startTile, goalTile);
        open.Enqueue(
            startTile,
            (startHeuristic, startHeuristic, startTile.X, startTile.Y));

        var expanded = 0;
        var closed = new HashSet<(int X, int Y)>();
        while (open.TryDequeue(out var current, out _))
        {
            if (!closed.Add(current))
            {
                continue;
            }

            if (TileDistance(current, goalTile) <= arrivalRadiusTiles)
            {
                return Reconstruct(mover, cameFrom, anchors, startTile, current, expanded);
            }

            expanded++;
            if (expanded > nodeBudget)
            {
                return PathResult.Refused(
                    PathFailure.BudgetExhausted,
                    expanded,
                    $"No route for {mover.Name} was found within " +
                    $"{nodeBudget} tiles of search.");
            }

            var from = anchors[current];
            var cost = best[current];
            foreach (var (deltaX, deltaY) in Neighbours)
            {
                var tile = (X: current.X + deltaX, Y: current.Y + deltaY);
                if (closed.Contains(tile))
                {
                    continue;
                }

                if (Landing(map, mover, from, deltaX, deltaY, reach) is not { } landing)
                {
                    continue;
                }

                var next = checked(cost + 1);
                if (best.TryGetValue(tile, out var known) && known <= next)
                {
                    continue;
                }

                best[tile] = next;
                anchors[tile] = landing;
                cameFrom[tile] = current;
                var heuristic = TileDistance(tile, goalTile);
                open.Enqueue(
                    tile,
                    (checked(next + heuristic), heuristic, tile.X, tile.Y));
            }
        }

        return PathResult.Refused(
            PathFailure.Unreachable,
            expanded,
            $"{mover.Name} has no route to its destination.");
    }

    /// <summary>
    /// Where a one-tile step from <paramref name="from"/> would actually put the
    /// mover, or nothing if the map refuses to hold it there.
    /// </summary>
    /// <remarks>
    /// This mirrors <see cref="MovementSolver"/>'s three questions in the same
    /// order — what surface is under the destination within a step's climb, does
    /// the drop onto it stay within a step, and does the footprint fit — without
    /// the swept contact test, which only decides <em>where along</em> a move
    /// contact happened. A tile step either fits at its far end or is not a step.
    /// </remarks>
    private static Vec3i? Landing(
        WorldMap map,
        WorldObject mover,
        Vec3i from,
        int deltaX,
        int deltaY,
        int reach)
    {
        var target = new Vec3i(
            checked(from.X + (deltaX * WorldMap.WorldUnitsPerTile)),
            checked(from.Y + (deltaY * WorldMap.WorldUnitsPerTile)),
            from.Z);
        var probe = At(mover, target);
        var support = map.FindSupport(probe, checked(target.Z + reach));
        if (support.IsNone)
        {
            // Nothing to stand on. Walking off an edge is a legal move for a
            // body that has decided to; it is never a leg of a route.
            return null;
        }

        if (checked(from.Z - support.TopZ) > reach)
        {
            return null;
        }

        var landing = target with { Z = support.TopZ };
        return map.ValidatePlacement(At(mover, landing), mover.Location).Allowed
            ? landing
            : null;
    }

    private static PathResult Reconstruct(
        WorldObject mover,
        Dictionary<(int X, int Y), (int X, int Y)> cameFrom,
        Dictionary<(int X, int Y), Vec3i> anchors,
        (int X, int Y) start,
        (int X, int Y) goal,
        int expanded)
    {
        var steps = new List<Vec3i>();
        var tile = goal;
        while (tile != start)
        {
            steps.Add(anchors[tile]);
            tile = cameFrom[tile];
        }

        steps.Reverse();
        return new PathResult(
            true,
            steps,
            expanded,
            PathFailure.None,
            $"{mover.Name} has a {steps.Count}-tile route.");
    }

    private static WorldObject At(WorldObject value, Vec3i position) =>
        value with
        {
            Location = ObjectLocation.OnMap(value.Location.MapId, position),
        };

    private static (int X, int Y) TileOf(Vec3i anchor) =>
        (FloorDiv(anchor.X, WorldMap.WorldUnitsPerTile),
            FloorDiv(anchor.Y, WorldMap.WorldUnitsPerTile));

    private static int TileDistance((int X, int Y) a, (int X, int Y) b) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor != 0 && value < 0 ? quotient - 1 : quotient;
    }
}

using Ash.Core;

namespace Ash.Sim;

/// <summary>What an NPC is doing when nothing is trying to kill it.</summary>
public enum ScheduleActivity : byte
{
    /// <summary>Standing where it belongs.</summary>
    Post = 0,

    /// <summary>Walking a route for its own sake.</summary>
    Patrol = 1,

    /// <summary>Away from its post doing work.</summary>
    Work = 2,

    /// <summary>Off duty and social.</summary>
    Rest = 3,

    /// <summary>Asleep. Reaching the bed is the whole activity.</summary>
    Sleep = 4,
}

/// <summary>Why a scheduled destination was not the one used.</summary>
public enum ScheduleFallback : byte
{
    /// <summary>The entry's own destination was reachable.</summary>
    None = 0,

    /// <summary>A later destination in the entry's chain was used instead.</summary>
    Alternate = 1,

    /// <summary>Nothing in the chain could be reached; the actor holds where it is.</summary>
    HoldPosition = 2,
}

/// <summary>
/// One block of an NPC's day: when it applies, what the NPC is doing, and where.
/// </summary>
/// <remarks>
/// AI-011 wants an activity, a destination and an activation condition on every
/// entry, and AI-014 wants a defined answer when the destination cannot be
/// reached. Both live here: <see cref="Destinations"/> is a chain, ordered
/// best-first, and a blocked route falls to the next one rather than leaving the
/// NPC pushing at a wall for the rest of the day. An empty chain — or a chain
/// that is entirely blocked — is not a stuck process; it is the defined
/// hold-position outcome.
///
/// Destinations are offsets from the NPC's anchor rather than absolute anchors,
/// so one authored routine serves every guard in the world without a copy per
/// post.
/// </remarks>
public sealed record ScheduleEntry(
    int FromHour,
    int ToHour,
    ScheduleActivity Activity,
    IReadOnlyList<(int TileX, int TileY)> Destinations)
{
    public bool AppliesAt(int hour) => WorldCalendar.InWindow(hour, FromHour, ToHour);

    public void Validate()
    {
        if (FromHour is < 0 or >= WorldCalendar.HoursPerDay ||
            ToHour is < 0 or > WorldCalendar.HoursPerDay ||
            FromHour == ToHour)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FromHour),
                $"{FromHour}-{ToHour}",
                "A schedule entry covers a non-empty window inside one day.");
        }

        if (!Enum.IsDefined(Activity))
        {
            throw new ArgumentOutOfRangeException(nameof(Activity));
        }

        ArgumentNullException.ThrowIfNull(Destinations);
    }
}

/// <summary>A named daily routine. Entries are tried in order; the first that applies wins.</summary>
public sealed record NpcRoutine(string Id, string Name, IReadOnlyList<ScheduleEntry> Entries)
{
    public ScheduleEntry EntryAt(int hour)
    {
        foreach (var entry in Entries)
        {
            if (entry.AppliesAt(hour))
            {
                return entry;
            }
        }

        throw new InvalidOperationException(
            $"Routine '{Id}' has no entry covering hour {hour}. A routine must " +
            "cover the whole day.");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) ||
            !Id.StartsWith("routine.", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A routine needs a routine.* id and a name.", nameof(Id));
        }

        if (Entries is null || Entries.Count == 0)
        {
            throw new ArgumentException("A routine needs entries.", nameof(Entries));
        }

        foreach (var entry in Entries)
        {
            entry.Validate();
        }

        // Every hour must be covered, or the NPC has a gap in its day and the
        // first tick inside it throws. Checking the whole day here means the
        // content is wrong at construction rather than at 3am on day four.
        for (var hour = 0; hour < WorldCalendar.HoursPerDay; hour++)
        {
            if (!Entries.Any(entry => entry.AppliesAt(hour)))
            {
                throw new ArgumentException(
                    $"Routine '{Id}' does not cover hour {hour}.",
                    nameof(Entries));
            }
        }
    }
}

public static class NpcRoutineCatalog
{
    public const string SentryId = "routine.sentry";
    public const string ForagerId = "routine.forager";
    public const string DenId = "routine.den";

    /// <summary>Holds a post by day, sleeps beside it by night.</summary>
    public static readonly NpcRoutine Sentry = new(
        SentryId,
        "Sentry",
        [
            new ScheduleEntry(6, 22, ScheduleActivity.Post, [(0, 0)]),
            new ScheduleEntry(22, 6, ScheduleActivity.Sleep, [(0, 1), (0, 0)]),
        ]);

    /// <summary>Ranges out to forage in daylight and comes home at dusk.</summary>
    public static readonly NpcRoutine Forager = new(
        ForagerId,
        "Forager",
        [
            new ScheduleEntry(7, 12, ScheduleActivity.Work, [(4, 0), (2, 0), (0, 0)]),
            new ScheduleEntry(12, 18, ScheduleActivity.Work, [(0, 4), (0, 2), (0, 0)]),
            new ScheduleEntry(18, 22, ScheduleActivity.Rest, [(0, 0)]),
            new ScheduleEntry(22, 7, ScheduleActivity.Sleep, [(0, 0)]),
        ]);

    /// <summary>Nocturnal: out at night, back in the den through the day.</summary>
    public static readonly NpcRoutine Den = new(
        DenId,
        "Den dweller",
        [
            new ScheduleEntry(20, 5, ScheduleActivity.Patrol, [(3, 3), (1, 1), (0, 0)]),
            new ScheduleEntry(5, 20, ScheduleActivity.Sleep, [(0, 0)]),
        ]);

    private static readonly IReadOnlyDictionary<string, NpcRoutine> Index = Build();

    public static IReadOnlyList<NpcRoutine> All =>
        Index.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();

    public static NpcRoutine Get(string routineId) =>
        Index.TryGetValue(routineId, out var routine)
            ? routine
            : throw new InvalidOperationException($"No routine '{routineId}' is authored.");

    public static bool TryGet(string routineId, out NpcRoutine routine) =>
        Index.TryGetValue(routineId, out routine!);

    private static IReadOnlyDictionary<string, NpcRoutine> Build()
    {
        var index = new Dictionary<string, NpcRoutine>(StringComparer.Ordinal);
        foreach (var routine in new[] { Sentry, Forager, Den })
        {
            routine.Validate();
            index.Add(routine.Id, routine);
        }

        return index;
    }
}

/// <summary>One NPC's schedule state, exactly as a save carries it (AI-013).</summary>
public readonly record struct NpcScheduleSnapshot(
    ObjectId ActorId,
    string RoutineId,
    Vec3i Anchor,
    ScheduleActivity Activity,
    ScheduleFallback Fallback,
    Vec3i Destination,
    long StepReadyAtTick,

    /// <summary>
    /// Zero unless a script has taken the NPC off its routine (AI-012); while
    /// set, the schedule leaves the actor alone until this beat.
    /// </summary>
    long SuspendedUntilTick);

/// <summary>What one NPC did on one scheduled beat, for the log and for tests.</summary>
public sealed record ScheduleStep(
    ObjectId ActorId,
    ScheduleActivity Activity,
    ScheduleFallback Fallback,
    Vec3i Destination,
    bool Moved,
    string Message);

/// <summary>
/// Daily routines for NPCs nobody is fighting.
/// </summary>
/// <remarks>
/// This drives only actors the combat director is not driving. A creature that
/// has noticed the player has stopped keeping to its day, and two systems both
/// spending its movement in the same beat would let it cross twice the ground a
/// round allows.
///
/// Routing is the same <see cref="SpatialPathfinder"/> combat uses, so a scheduled
/// walk obeys the crates the player left in the corridor (AI-001, AI-002).
/// </remarks>
public sealed class NpcScheduleService
{
    private readonly ObjectStore _objects;
    private readonly ObjectTransferService _transfers;
    private readonly SpatialPathfinder _pathfinder;
    private readonly CombatClock _clock;
    private readonly ActorConditionService _conditions;
    private readonly Dictionary<ObjectId, NpcScheduleSnapshot> _states = [];

    public NpcScheduleService(
        ObjectStore objects,
        ObjectTransferService transfers,
        SpatialPathfinder pathfinder,
        CombatClock clock,
        ActorConditionService conditions)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        _pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
    }

    public IReadOnlyList<NpcScheduleSnapshot> Scheduled =>
        _states.Values.OrderBy(value => value.ActorId).ToArray();

    public NpcScheduleSnapshot? StateOf(ObjectId actorId) =>
        _states.TryGetValue(actorId, out var state) ? state : null;

    /// <summary>
    /// Puts an actor on a routine, anchored where it currently stands unless a
    /// different anchor is named.
    /// </summary>
    public NpcScheduleSnapshot Assign(
        ObjectId actorId,
        string routineId,
        Vec3i? anchor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routineId);
        _ = NpcRoutineCatalog.Get(routineId);
        var actor = _objects.Get(actorId);
        if (!actor.HasFlag(ObjectFlags.Actor))
        {
            throw new ArgumentException(
                $"{actor.Name} is not an actor and keeps no schedule.",
                nameof(actorId));
        }

        if (actor.Location.Kind != LocationKind.OnMap)
        {
            throw new InvalidOperationException(
                $"{actor.Name} is not on a map, so its routine has no anchor.");
        }

        var state = new NpcScheduleSnapshot(
            actorId,
            routineId,
            anchor ?? actor.Location.Position,
            ScheduleActivity.Post,
            ScheduleFallback.None,
            anchor ?? actor.Location.Position,
            _clock.Tick,
            SuspendedUntilTick: 0);
        _states[actorId] = state;
        return state;
    }

    public bool Release(ObjectId actorId) => _states.Remove(actorId);

    /// <summary>
    /// Takes an actor off its routine until <paramref name="untilTick"/>, for a
    /// script that owns it for a while (AI-012).
    /// </summary>
    public void Suspend(ObjectId actorId, long untilTick)
    {
        if (untilTick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(untilTick));
        }

        if (!_states.TryGetValue(actorId, out var state))
        {
            throw new InvalidOperationException($"{actorId} keeps no schedule.");
        }

        _states[actorId] = state with { SuspendedUntilTick = untilTick };
    }

    /// <summary>
    /// Advances every scheduled actor one beat, skipping those
    /// <paramref name="busy"/> reports as otherwise occupied.
    /// </summary>
    public IReadOnlyList<ScheduleStep> Advance(Func<ObjectId, bool>? busy = null)
    {
        var steps = new List<ScheduleStep>();
        foreach (var actorId in _states.Keys.Order().ToArray())
        {
            var state = _states[actorId];
            if (!_objects.TryGet(actorId, out var actor) || !actor.IsAlive ||
                actor.Location.Kind != LocationKind.OnMap)
            {
                _states.Remove(actorId);
                continue;
            }

            if (_clock.Tick < state.SuspendedUntilTick || busy?.Invoke(actorId) == true)
            {
                continue;
            }

            steps.Add(Advance(actor, state));
        }

        return steps;
    }

    public void Restore(IEnumerable<NpcScheduleSnapshot> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        _states.Clear();
        foreach (var state in states.OrderBy(value => value.ActorId))
        {
            if (!NpcRoutineCatalog.TryGet(state.RoutineId, out _) ||
                !Enum.IsDefined(state.Activity) || !Enum.IsDefined(state.Fallback) ||
                state.StepReadyAtTick < 0 || state.SuspendedUntilTick < 0 ||
                !_objects.TryGet(state.ActorId, out var actor) ||
                !actor.HasFlag(ObjectFlags.Actor) ||
                !_states.TryAdd(state.ActorId, state))
            {
                throw new ObjectWorldSaveException(
                    $"The saved schedule for {state.ActorId} is invalid.");
            }
        }
    }

    private ScheduleStep Advance(WorldObject actor, NpcScheduleSnapshot state)
    {
        var routine = NpcRoutineCatalog.Get(state.RoutineId);
        var entry = routine.EntryAt(WorldCalendar.HourOf(_clock.Tick));

        // Routing costs a real search, so it is asked only on a beat the actor
        // could actually spend on a step. Between steps the entry alone decides
        // what the actor is doing, and that is a lookup.
        if (_clock.Tick < state.StepReadyAtTick || _conditions.PreventsMovement(actor.Id))
        {
            _states[actor.Id] = state with { Activity = entry.Activity };
            return new ScheduleStep(
                actor.Id, entry.Activity, state.Fallback, state.Destination,
                Moved: false, $"{actor.Name} waits.");
        }

        var (destination, fallback, route) = Resolve(actor, state.Anchor, entry);
        state = state with
        {
            Activity = entry.Activity,
            Fallback = fallback,
            Destination = destination,
        };

        if (fallback == ScheduleFallback.HoldPosition)
        {
            _states[actor.Id] = state;
            return new ScheduleStep(
                actor.Id,
                entry.Activity,
                fallback,
                destination,
                Moved: false,
                $"{actor.Name} cannot reach anywhere its {entry.Activity.ToString().ToLowerInvariant()} " +
                "would take it, and holds position.");
        }

        if (actor.Location.Position == destination)
        {
            _states[actor.Id] = state;
            return new ScheduleStep(
                actor.Id, entry.Activity, fallback, destination, Moved: false,
                $"{actor.Name} is where its day says it should be.");
        }

        var stepsPerRound = Math.Max(
            1,
            _conditions.MovementStepsPerRound(actor.Id, StepsPerRoundOf(actor)));
        state = state with
        {
            StepReadyAtTick = checked(
                _clock.Tick +
                (CombatClock.BeatsIn(Ash.Rules.AttackSpeed.RoundMilliseconds) / stepsPerRound)),
        };

        if (route is not { Found: true, Steps.Count: > 0 })
        {
            _states[actor.Id] = state with { Fallback = ScheduleFallback.HoldPosition };
            return new ScheduleStep(
                actor.Id, entry.Activity, ScheduleFallback.HoldPosition, destination,
                Moved: false,
                route?.Message ?? $"{actor.Name} has nowhere to walk to.");
        }

        var moved = _transfers.Execute(new ObjectTransferRequest(
            actor.Id,
            actor.Location,
            ObjectLocation.OnMap(actor.Location.MapId, route.Steps[0])));
        _states[actor.Id] = state;
        return new ScheduleStep(
            actor.Id, entry.Activity, fallback, destination, moved.Succeeded,
            moved.Succeeded
                ? $"{actor.Name} walks toward its {entry.Activity.ToString().ToLowerInvariant()}."
                : moved.Message);
    }

    /// <summary>
    /// The first destination in the entry's chain the actor can actually get to,
    /// and how far down the chain that was (AI-014).
    /// </summary>
    private (Vec3i Destination, ScheduleFallback Fallback, PathResult? Route) Resolve(
        WorldObject actor,
        Vec3i anchor,
        ScheduleEntry entry)
    {
        for (var index = 0; index < entry.Destinations.Count; index++)
        {
            var (tileX, tileY) = entry.Destinations[index];
            var destination = new Vec3i(
                checked(anchor.X + (tileX * WorldMap.WorldUnitsPerTile)),
                checked(anchor.Y + (tileY * WorldMap.WorldUnitsPerTile)),
                anchor.Z);
            var fallback = index == 0
                ? ScheduleFallback.None
                : ScheduleFallback.Alternate;
            if (actor.Location.Position == destination)
            {
                return (destination, fallback, null);
            }

            var route = _pathfinder.FindPath(actor.Id, destination);
            if (route.Found)
            {
                return (destination, fallback, route);
            }
        }

        return (actor.Location.Position, ScheduleFallback.HoldPosition, null);
    }

    private static int StepsPerRoundOf(WorldObject actor) =>
        MonsterCatalog.TryGet(actor.TypeId, out var profile)
            ? profile.MovementStepsPerRound
            : Ash.Rules.MovementAllowance.StepsPerRound;
}

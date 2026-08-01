using Ash.Rules;

namespace Ash.Sim;

/// <summary>A durable mechanical state applied by a trauma result.</summary>
public sealed record ActorCondition(
    TraumaEffectKind Kind,
    ObjectId SourceId,
    int Magnitude,
    long AppliedTick,
    long? ExpiresAtTick,
    long? NextPeriodicTick,
    string? Detail = null,
    string PresentationKey = "condition");

public sealed record ActorConditionSnapshot(
    ObjectId ActorId,
    TraumaEffectKind Kind,
    ObjectId SourceId,
    int Magnitude,
    long AppliedTick,
    long? ExpiresAtTick,
    long? NextPeriodicTick,
    string? Detail,
    string PresentationKey);

public readonly record struct ActorConditionMutation(
    ObjectId ActorId,
    ObjectId SourceId,
    TraumaEffect Effect,
    long AppliedTick);

/// <summary>Central conversion between authored trauma time and combat beats.</summary>
public static class ConditionTiming
{
    public const int BeatsPerRound = AttackSpeed.RoundMilliseconds / CombatClock.TickMilliseconds;
    public const int BeatsPerHour = 60 * 60 * 1000 / CombatClock.TickMilliseconds;

    public static long? ExpiryTick(long now, TraumaEffect effect) => effect.DurationUnit switch
    {
        TraumaDurationUnit.None or TraumaDurationUnit.UntilHealed or TraumaDurationUnit.Permanent => null,
        TraumaDurationUnit.Rounds => checked(now + ((long)effect.Duration * BeatsPerRound)),
        TraumaDurationUnit.Hours => checked(now + ((long)effect.Duration * BeatsPerHour)),
        TraumaDurationUnit.D4Hours => checked(now + ((long)effect.Duration * BeatsPerHour)),
        _ => throw new ArgumentOutOfRangeException(nameof(effect)),
    };
}

/// <summary>
/// Owns deterministic condition application, replacement and expiry. This is
/// deliberately separate from item condition/durability on <see cref="WorldObject"/>.
/// </summary>
public sealed class ActorConditionService
{
    private readonly Dictionary<ObjectId, List<ActorCondition>> _byActor = [];

    public IReadOnlyList<ActorCondition> Of(ObjectId actorId) =>
        _byActor.TryGetValue(actorId, out var conditions)
            ? conditions.OrderBy(condition => condition.AppliedTick).ThenBy(condition => condition.Kind).ToArray()
            : [];

    public bool Has(ObjectId actorId, TraumaEffectKind kind) =>
        Of(actorId).Any(condition => condition.Kind == kind);

    public bool PreventsAction(ObjectId actorId) => Of(actorId).Any(condition =>
        condition.Kind is TraumaEffectKind.Stun or
            TraumaEffectKind.Incapacitated or TraumaEffectKind.Unconscious or
            TraumaEffectKind.Paralyzed or TraumaEffectKind.Dying or
            TraumaEffectKind.StableAtZero);

    public bool PreventsMovement(ObjectId actorId) => Of(actorId).Any(condition =>
        condition.Kind is TraumaEffectKind.Restrained or TraumaEffectKind.Incapacitated or
            TraumaEffectKind.Unconscious or TraumaEffectKind.Paralyzed or
            TraumaEffectKind.Dying or TraumaEffectKind.StableAtZero);

    public int MovementStepsPerRound(ObjectId actorId, int normalSteps)
    {
        var conditions = Of(actorId);
        if (PreventsMovement(actorId))
        {
            return 0;
        }

        var halved = conditions.Any(condition => condition.Kind is
            TraumaEffectKind.Prone or TraumaEffectKind.Injured);
        var slowFeet = conditions
            .Where(condition => condition.Kind == TraumaEffectKind.Slow)
            .Sum(condition => condition.Magnitude > 0 ? condition.Magnitude : 10);
        var steps = halved ? Math.Max(1, normalSteps / 2) : normalSteps;
        return Math.Max(1, steps - ((slowFeet + 4) / 5));
    }

    public void Apply(ObjectId targetId, ObjectId sourceId, TraumaEffect effect, long now)
    {
        if (!CanStore(effect.Kind))
        {
            throw new NotSupportedException(
                $"{effect.Kind} requires its dedicated combat mutation and cannot be stored as an actor condition.");
        }

        var conditions = _byActor.GetValueOrDefault(targetId) ?? [];
        _byActor[targetId] = conditions;
        conditions.RemoveAll(condition => condition.Kind == effect.Kind && condition.SourceId == sourceId);
        conditions.Add(new ActorCondition(
            effect.Kind, sourceId, effect.Magnitude, now,
            ConditionTiming.ExpiryTick(now, effect),
            effect.Kind is TraumaEffectKind.Bleeding or TraumaEffectKind.Suffocating
                ? checked(now + ConditionTiming.BeatsPerRound)
                : null,
            effect.Detail,
            $"condition.{effect.Kind.ToString().ToLowerInvariant()}"));
    }

    public IReadOnlyList<(ObjectId ActorId, int Damage)> AdvanceTo(long tick)
    {
        var periodic = new List<(ObjectId, int)>();
        foreach (var (actorId, conditions) in _byActor.OrderBy(pair => pair.Key))
        {
            for (var index = 0; index < conditions.Count; index++)
            {
                var condition = conditions[index];
                while (condition.Kind is TraumaEffectKind.Bleeding or TraumaEffectKind.Suffocating &&
                       condition.NextPeriodicTick is { } due && due <= tick)
                {
                    periodic.Add((actorId, Math.Max(1, condition.Magnitude)));

                    condition = condition with
                    {
                        NextPeriodicTick = checked(due + ConditionTiming.BeatsPerRound),
                    };
                }

                conditions[index] = condition;
            }

            conditions.RemoveAll(condition => condition.ExpiresAtTick is { } expiry && expiry <= tick);
        }

        return periodic;
    }

    public bool Remove(ObjectId actorId, TraumaEffectKind kind) =>
        _byActor.TryGetValue(actorId, out var conditions) &&
        conditions.RemoveAll(condition => condition.Kind == kind) > 0;

    public bool RemoveAll(ObjectId actorId) => _byActor.Remove(actorId);

    public int RemoveHealed(ObjectId actorId)
    {
        if (!_byActor.TryGetValue(actorId, out var conditions))
        {
            return 0;
        }

        return conditions.RemoveAll(condition => condition.Kind is
            TraumaEffectKind.Bleeding or TraumaEffectKind.Injured or
            TraumaEffectKind.Exhaustion or TraumaEffectKind.BreakBone or
            TraumaEffectKind.DisableLimb or TraumaEffectKind.Paralyzed);
    }

    public bool Consume(ObjectId actorId, TraumaEffectKind kind, ObjectId? sourceId = null)
    {
        if (!_byActor.TryGetValue(actorId, out var conditions))
        {
            return false;
        }

        var index = conditions.FindIndex(condition =>
            condition.Kind == kind &&
            (!sourceId.HasValue || condition.SourceId == sourceId.Value));
        if (index < 0)
        {
            return false;
        }

        conditions.RemoveAt(index);
        return true;
    }

    public IReadOnlyList<ActorConditionSnapshot> Capture() => _byActor
        .OrderBy(pair => pair.Key)
        .SelectMany(pair => pair.Value
            .OrderBy(condition => condition.AppliedTick)
            .ThenBy(condition => condition.Kind)
            .Select(condition => new ActorConditionSnapshot(
                pair.Key,
                condition.Kind,
                condition.SourceId,
                condition.Magnitude,
                condition.AppliedTick,
                condition.ExpiresAtTick,
                condition.NextPeriodicTick,
                condition.Detail,
                condition.PresentationKey)))
        .ToArray();

    public void Restore(IEnumerable<ActorConditionSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        _byActor.Clear();
        foreach (var value in snapshots)
        {
            if (!CanStore(value.Kind))
            {
                throw new InvalidOperationException($"Cannot restore unsupported condition {value.Kind}.");
            }

            var conditions = _byActor.GetValueOrDefault(value.ActorId) ?? [];
            _byActor[value.ActorId] = conditions;
            conditions.Add(new ActorCondition(
                value.Kind,
                value.SourceId,
                value.Magnitude,
                value.AppliedTick,
                value.ExpiresAtTick,
                value.NextPeriodicTick,
                value.Detail,
                value.PresentationKey));
        }
    }

    public static bool CanStore(TraumaEffectKind kind) => kind is
        TraumaEffectKind.Bleeding or TraumaEffectKind.ActivityPenalty or
        TraumaEffectKind.Stun or TraumaEffectKind.Prone or TraumaEffectKind.Unconscious or
        TraumaEffectKind.Paralyzed or TraumaEffectKind.Restrained or
        TraumaEffectKind.Incapacitated or TraumaEffectKind.Dying or
        TraumaEffectKind.Suffocating or TraumaEffectKind.Exhaustion or
        TraumaEffectKind.Injured or TraumaEffectKind.StableAtZero or
        TraumaEffectKind.Sap or TraumaEffectKind.Vex or TraumaEffectKind.Slow or
        TraumaEffectKind.BreakBone or TraumaEffectKind.DisableLimb or
        TraumaEffectKind.DestroyEye or TraumaEffectKind.Cleave;
}

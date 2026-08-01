using Ash.Rules;

namespace Ash.Sim;

public enum ActorConditionRemovalPolicy
{
    Explicit,
    Timed,
    UntilHealed,
    Consumed,
    RecoverAtExpiry,
    Permanent,
}

public enum ActorConditionStackingPolicy
{
    ReplaceBySource,
    StackMagnitudeBySource,
    IndependentByDetail,
}

/// <summary>A durable mechanical state applied by a trauma result.</summary>
public sealed record ActorCondition(
    TraumaEffectKind Kind,
    ObjectId SourceId,
    int Magnitude,
    long AppliedTick,
    long? ExpiresAtTick,
    long? NextPeriodicTick,
    ActorConditionRemovalPolicy RemovalPolicy,
    ActorConditionStackingPolicy StackingPolicy,
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
    ActorConditionRemovalPolicy RemovalPolicy,
    ActorConditionStackingPolicy StackingPolicy,
    string? Detail,
    string PresentationKey);

public readonly record struct ActorConditionMutation(
    ObjectId ActorId,
    ObjectId SourceId,
    TraumaEffect Effect,
    long AppliedTick);

/// <summary>A one-use actor condition spent by the same combat transaction.</summary>
public readonly record struct ActorConditionConsumption(
    ObjectId ActorId,
    TraumaEffectKind Kind,
    ObjectId? SourceId = null);

public readonly record struct ActorConditionPeriodicEffect(
    ObjectId ActorId,
    TraumaEffectKind Kind,
    int Magnitude);

public readonly record struct ActorConditionRecovery(
    ObjectId ActorId,
    int RestoredHits);

public sealed record ActorConditionAdvance(
    IReadOnlyList<ActorConditionPeriodicEffect> PeriodicEffects,
    IReadOnlyList<ActorConditionRecovery> Recoveries);

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
        TraumaDurationUnit.D4Hours => throw new InvalidOperationException(
            "A d4-hour duration must be rolled from the saved world dice before condition application."),
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

    public bool Has(
        ObjectId actorId,
        TraumaEffectKind kind,
        ObjectId? sourceId) => Of(actorId).Any(condition =>
            condition.Kind == kind &&
            (!sourceId.HasValue || condition.SourceId == sourceId.Value));

    public bool PreventsAction(ObjectId actorId) => Of(actorId).Any(condition =>
        condition.Kind is TraumaEffectKind.Stun or
            TraumaEffectKind.Incapacitated or TraumaEffectKind.Unconscious or
            TraumaEffectKind.Paralyzed or
            TraumaEffectKind.StableAtZero);

    public bool PreventsMovement(ObjectId actorId) => Of(actorId).Any(condition =>
        condition.Kind is TraumaEffectKind.Restrained or TraumaEffectKind.Incapacitated or
            TraumaEffectKind.Unconscious or TraumaEffectKind.Paralyzed or
            TraumaEffectKind.StableAtZero);

    public int MovementStepsPerRound(ObjectId actorId, int normalSteps)
    {
        var conditions = Of(actorId);
        if (PreventsMovement(actorId))
        {
            return 0;
        }

        var halved = conditions.Any(condition => condition.Kind is
            TraumaEffectKind.Prone or TraumaEffectKind.Injured ||
            condition.Kind is TraumaEffectKind.BreakBone or TraumaEffectKind.DisableLimb &&
            IsLowerBody(condition.Detail));
        var slowFeet = conditions
            .Where(condition => condition.Kind == TraumaEffectKind.Slow)
            .Sum(condition => condition.Magnitude > 0 ? condition.Magnitude : 10);
        var exhaustionSteps = conditions
            .Where(condition => condition.Kind == TraumaEffectKind.Exhaustion)
            .Sum(condition => Math.Max(1, condition.Magnitude));
        var steps = halved ? Math.Max(1, normalSteps / 2) : normalSteps;
        return Math.Max(1, steps - ((slowFeet + 4) / 5) - exhaustionSteps);
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
        var stacking = StackingPolicyFor(effect.Kind);
        var removal = RemovalPolicyFor(effect);
        var next = new ActorCondition(
            effect.Kind, sourceId, effect.Magnitude, now,
            ConditionTiming.ExpiryTick(now, effect),
            effect.Kind is TraumaEffectKind.Bleeding or TraumaEffectKind.Suffocating
                ? checked(now + ConditionTiming.BeatsPerRound)
                : null,
            removal,
            stacking,
            effect.Detail,
            $"condition.{effect.Kind.ToString().ToLowerInvariant()}");

        var matching = conditions.FindIndex(condition =>
            condition.Kind == effect.Kind && condition.SourceId == sourceId &&
            (stacking != ActorConditionStackingPolicy.IndependentByDetail ||
             string.Equals(condition.Detail, effect.Detail, StringComparison.Ordinal)));
        if (matching < 0)
        {
            conditions.Add(next);
            return;
        }

        if (stacking == ActorConditionStackingPolicy.StackMagnitudeBySource)
        {
            var current = conditions[matching];
            conditions[matching] = next with
            {
                Magnitude = checked(Math.Max(1, current.Magnitude) + Math.Max(1, next.Magnitude)),
                NextPeriodicTick = Earliest(current.NextPeriodicTick, next.NextPeriodicTick),
            };
            return;
        }

        conditions[matching] = next;
    }

    public ActorConditionAdvance AdvanceTo(long tick)
    {
        var periodic = new List<ActorConditionPeriodicEffect>();
        var recoveries = new List<ActorConditionRecovery>();
        foreach (var (actorId, conditions) in _byActor.OrderBy(pair => pair.Key))
        {
            for (var index = 0; index < conditions.Count; index++)
            {
                var condition = conditions[index];
                while (condition.Kind is TraumaEffectKind.Bleeding or TraumaEffectKind.Suffocating &&
                       condition.NextPeriodicTick is { } due && due <= tick)
                {
                    periodic.Add(new ActorConditionPeriodicEffect(
                        actorId, condition.Kind, Math.Max(1, condition.Magnitude)));

                    condition = condition with
                    {
                        NextPeriodicTick = checked(due + ConditionTiming.BeatsPerRound),
                    };
                }

                conditions[index] = condition;
            }

            foreach (var condition in conditions.Where(condition =>
                         condition.RemovalPolicy == ActorConditionRemovalPolicy.RecoverAtExpiry &&
                         condition.ExpiresAtTick is { } expiry && expiry <= tick))
            {
                recoveries.Add(new ActorConditionRecovery(
                    actorId, Math.Max(1, condition.Magnitude)));
            }

            conditions.RemoveAll(condition =>
                condition.RemovalPolicy is ActorConditionRemovalPolicy.Timed or
                    ActorConditionRemovalPolicy.RecoverAtExpiry &&
                condition.ExpiresAtTick is { } expiry && expiry <= tick);
        }

        return new ActorConditionAdvance(periodic, recoveries);
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

        return conditions.RemoveAll(condition =>
            condition.RemovalPolicy == ActorConditionRemovalPolicy.UntilHealed);
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
                condition.RemovalPolicy,
                condition.StackingPolicy,
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
                value.RemovalPolicy,
                value.StackingPolicy,
                value.Detail,
                value.PresentationKey));
        }
    }

    public static ActorConditionRemovalPolicy RemovalPolicyFor(TraumaEffect effect)
    {
        if (effect.Kind == TraumaEffectKind.StableAtZero)
        {
            return ActorConditionRemovalPolicy.RecoverAtExpiry;
        }

        if (effect.Kind is TraumaEffectKind.Sap or TraumaEffectKind.Vex or TraumaEffectKind.Cleave)
        {
            return ActorConditionRemovalPolicy.Consumed;
        }

        if (effect.DurationUnit == TraumaDurationUnit.Permanent ||
            effect.Kind == TraumaEffectKind.DestroyEye)
        {
            return ActorConditionRemovalPolicy.Permanent;
        }

        if (effect.DurationUnit is TraumaDurationUnit.Rounds or TraumaDurationUnit.Hours)
        {
            return ActorConditionRemovalPolicy.Timed;
        }

        if (effect.DurationUnit == TraumaDurationUnit.UntilHealed || effect.Kind is
            TraumaEffectKind.Bleeding or TraumaEffectKind.Exhaustion or
            TraumaEffectKind.Injured or TraumaEffectKind.BreakBone or
            TraumaEffectKind.DisableLimb or TraumaEffectKind.Paralyzed)
        {
            return ActorConditionRemovalPolicy.UntilHealed;
        }

        return ActorConditionRemovalPolicy.Explicit;
    }

    public static ActorConditionStackingPolicy StackingPolicyFor(TraumaEffectKind kind) => kind switch
    {
        TraumaEffectKind.Bleeding or TraumaEffectKind.ActivityPenalty or
        TraumaEffectKind.Exhaustion or TraumaEffectKind.Injured or
        TraumaEffectKind.DestroyEye => ActorConditionStackingPolicy.StackMagnitudeBySource,
        TraumaEffectKind.BreakBone or TraumaEffectKind.DisableLimb =>
            ActorConditionStackingPolicy.IndependentByDetail,
        _ => ActorConditionStackingPolicy.ReplaceBySource,
    };

    private static long? Earliest(long? left, long? right) =>
        left is null ? right : right is null ? left : Math.Min(left.Value, right.Value);

    private static bool IsLowerBody(string? detail) => detail?.Contains(
        "leg", StringComparison.OrdinalIgnoreCase) == true ||
        detail?.Contains("knee", StringComparison.OrdinalIgnoreCase) == true ||
        detail?.Contains("ankle", StringComparison.OrdinalIgnoreCase) == true ||
        detail?.Contains("foot", StringComparison.OrdinalIgnoreCase) == true ||
        detail?.Contains("hip", StringComparison.OrdinalIgnoreCase) == true ||
        detail?.Contains("pelvis", StringComparison.OrdinalIgnoreCase) == true;

    public static bool CanStore(TraumaEffectKind kind) => kind is
        TraumaEffectKind.Bleeding or TraumaEffectKind.ActivityPenalty or
        TraumaEffectKind.Stun or TraumaEffectKind.Prone or TraumaEffectKind.Unconscious or
        TraumaEffectKind.Paralyzed or TraumaEffectKind.Restrained or
        TraumaEffectKind.Incapacitated or
        TraumaEffectKind.Suffocating or TraumaEffectKind.Exhaustion or
        TraumaEffectKind.Injured or TraumaEffectKind.StableAtZero or
        TraumaEffectKind.Sap or TraumaEffectKind.Vex or TraumaEffectKind.Slow or
        TraumaEffectKind.BreakBone or TraumaEffectKind.DisableLimb or
        TraumaEffectKind.DestroyEye or TraumaEffectKind.Cleave;
}

using Ash.Rules;
using Ash.Core;

namespace Ash.Sim;

/// <summary>Mechanical results applied after an attack table resolves.</summary>
public readonly record struct TraumaDispatchResult(
    int AdditionalDamage,
    bool Moved,
    IReadOnlyList<ObjectId> DroppedItems,
    IReadOnlyList<ObjectId> BrokenItems,
    bool CorpseCreated = false,
    bool CleaveGranted = false);

/// <summary>
/// Exhaustive runtime policy for every structured trauma kind. Effects which
/// require a dedicated mutation are applied here; durable actor states go to
/// <see cref="ActorConditionService"/>.
/// </summary>
public sealed class TraumaEffectDispatcher
{
    /// <summary>
    /// The reviewed runtime policy surface. The phase proof compares this
    /// explicit list with the enum so a newly parsed effect cannot silently
    /// ship through the dispatcher's default branch.
    /// </summary>
    public static IReadOnlyList<TraumaEffectKind> SupportedKinds { get; } =
    [
        TraumaEffectKind.AdditionalHits,
        TraumaEffectKind.Bleeding,
        TraumaEffectKind.ActivityPenalty,
        TraumaEffectKind.Stun,
        TraumaEffectKind.Prone,
        TraumaEffectKind.Unconscious,
        TraumaEffectKind.Death,
        TraumaEffectKind.ForcedMovement,
        TraumaEffectKind.DropHeldItem,
        TraumaEffectKind.BreakItem,
        TraumaEffectKind.BreakBone,
        TraumaEffectKind.DisableLimb,
        TraumaEffectKind.DestroyEye,
        TraumaEffectKind.Paralyzed,
        TraumaEffectKind.Restrained,
        TraumaEffectKind.Incapacitated,
        TraumaEffectKind.Dying,
        TraumaEffectKind.Suffocating,
        TraumaEffectKind.Exhaustion,
        TraumaEffectKind.Injured,
        TraumaEffectKind.StableAtZero,
        TraumaEffectKind.Graze,
        TraumaEffectKind.Vex,
        TraumaEffectKind.Sap,
        TraumaEffectKind.Topple,
        TraumaEffectKind.Cleave,
        TraumaEffectKind.Slow,
        TraumaEffectKind.Push,
    ];

    private readonly ObjectStore _objects;
    private readonly ObjectTransferService _transfers;
    private readonly ActorVitality _vitality;
    private readonly ActorConditionService _conditions;
    private readonly MovementSolver _movement;
    private readonly Func<long> _currentTick;
    private readonly Dice? _dice;

    public TraumaEffectDispatcher(
        ObjectStore objects,
        ObjectTransferService transfers,
        ActorVitality vitality,
        ActorConditionService conditions,
        MovementSolver movement,
        Func<long> currentTick,
        Dice? dice = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        _vitality = vitality ?? throw new ArgumentNullException(nameof(vitality));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _currentTick = currentTick ?? throw new ArgumentNullException(nameof(currentTick));
        _dice = dice;
    }

    public TraumaDispatchResult Apply(
        ObjectId attackerId,
        ObjectId targetId,
        IEnumerable<TraumaEffect> effects,
        InjuryState? baseInjury = null,
        IReadOnlyList<ActorConditionConsumption>? conditionConsumptions = null,
        Action<CombatMutationStage>? afterStage = null)
    {
        var diceRollback = _dice?.State;
        try
        {
            return ApplyCore(
                attackerId,
                targetId,
                effects,
                baseInjury,
                conditionConsumptions,
                afterStage);
        }
        catch
        {
            if (_dice is not null && diceRollback.HasValue)
            {
                _dice.RestoreState(diceRollback.Value);
            }

            throw;
        }
    }

    private TraumaDispatchResult ApplyCore(
        ObjectId attackerId,
        ObjectId targetId,
        IEnumerable<TraumaEffect> effects,
        InjuryState? baseInjury,
        IReadOnlyList<ActorConditionConsumption>? conditionConsumptions,
        Action<CombatMutationStage>? afterStage)
    {
        var additionalDamage = 0;
        var moved = false;
        var dropped = new List<ObjectId>();
        var broken = new List<ObjectId>();
        InjuryState? injury = baseInjury;
        var transfers = new List<ObjectTransferRequest>();
        var physics = new List<ObjectPhysicsUpdate>();
        var conditionChanges = new List<(ObjectId Actor, ObjectId Source, TraumaEffect Effect)>();
        var consumptions = conditionConsumptions?.ToList() ?? [];
        var cleaveGranted = false;
        foreach (var unresolvedEffect in effects)
        {
            var effect = ResolveDuration(unresolvedEffect);
            switch (effect.Kind)
            {
                case TraumaEffectKind.AdditionalHits:
                    // Combined with table concussion by CombatAttackService.
                    break;
                case TraumaEffectKind.Death:
                    injury = DeadState(targetId, injury);
                    break;
                case TraumaEffectKind.Dying:
                    injury = BodyState(targetId, VitalityState.Dying, injury);
                    break;
                case TraumaEffectKind.StableAtZero:
                    injury = BodyState(targetId, VitalityState.Stable, injury);
                    conditionChanges.Add((targetId, attackerId, effect));
                    break;
                case TraumaEffectKind.ForcedMovement:
                case TraumaEffectKind.Push:
                    if (PlanPushAway(attackerId, targetId, Math.Max(1, effect.Magnitude)) is { } push)
                    {
                        transfers.Add(push.Transfer);
                        physics.Add(push.Physics);
                        moved = true;
                    }
                    break;
                case TraumaEffectKind.DropHeldItem:
                    foreach (var drop in PlanDropHeld(targetId, effect.Detail))
                    {
                        transfers.Add(drop);
                        dropped.Add(drop.ObjectId);
                    }
                    break;
                case TraumaEffectKind.BreakItem:
                    broken.AddRange(PlanBreakEquipped(targetId, effect.Detail));
                    break;
                case TraumaEffectKind.Graze:
                    additionalDamage = checked(additionalDamage + Math.Max(0, effect.Magnitude));
                    break;
                case TraumaEffectKind.Topple:
                    conditionChanges.Add((
                        targetId, attackerId,
                        effect with { Kind = TraumaEffectKind.Prone }));
                    break;
                case TraumaEffectKind.Cleave:
                    if (HasLegalCleaveTarget(attackerId, targetId))
                    {
                        conditionChanges.Add((
                            attackerId,
                            targetId,
                            effect.DurationUnit == TraumaDurationUnit.None
                                ? effect with
                                {
                                    Duration = 1,
                                    DurationUnit = TraumaDurationUnit.Rounds,
                                }
                                : effect));
                        cleaveGranted = true;
                    }
                    break;
                case TraumaEffectKind.Bleeding:
                case TraumaEffectKind.ActivityPenalty:
                case TraumaEffectKind.Stun:
                case TraumaEffectKind.Prone:
                case TraumaEffectKind.Unconscious:
                case TraumaEffectKind.BreakBone:
                case TraumaEffectKind.DisableLimb:
                case TraumaEffectKind.DestroyEye:
                case TraumaEffectKind.Paralyzed:
                case TraumaEffectKind.Restrained:
                case TraumaEffectKind.Incapacitated:
                case TraumaEffectKind.Suffocating:
                case TraumaEffectKind.Exhaustion:
                case TraumaEffectKind.Injured:
                case TraumaEffectKind.Vex:
                case TraumaEffectKind.Sap:
                case TraumaEffectKind.Slow:
                    conditionChanges.Add((targetId, attackerId, effect));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(effects), effect.Kind, "Trauma kind has no runtime policy.");
            }
        }

        CombatTransform? transform = null;
        if (injury?.IsDead == true &&
            _objects.Get(targetId).HasFlag(ObjectFlags.Monster))
        {
            var body = _objects.Get(targetId);
            PlanCorpseTransfers(body, transfers);
            transform = new CombatTransform(
                targetId,
                $"remains.{body.TypeId}",
                $"Remains of {body.Name}",
                "container.corpse",
                ObjectFlags.Container | ObjectFlags.Corpse |
                ObjectFlags.Usable | ObjectFlags.Visible,
                Height: 24);
        }

        if (injury is not null || transfers.Count > 0 || broken.Count > 0 ||
            conditionChanges.Count > 0 || consumptions.Count > 0)
        {
            var conditionMutations = conditionChanges.Select(change =>
                new ActorConditionMutation(
                    change.Actor,
                    change.Source,
                    change.Effect,
                    _currentTick())).ToArray();
            _objects.CommitCombatMutation(
                new CombatMutation(
                    targetId,
                    injury,
                    transfers,
                    broken,
                    transform,
                    conditionMutations,
                    physics,
                    consumptions,
                    transform is null ? [] : [targetId]),
                _conditions,
                afterStage);
        }

        return new TraumaDispatchResult(
            additionalDamage,
            moved,
            dropped,
            broken,
            transform is not null,
            cleaveGranted);
    }

    private TraumaEffect ResolveDuration(TraumaEffect effect)
    {
        if (effect.DurationUnit != TraumaDurationUnit.D4Hours)
        {
            return effect;
        }

        if (_dice is null)
        {
            throw new InvalidOperationException(
                $"{effect.Kind} requires a saved world die to resolve its d4-hour duration.");
        }

        var diceCount = Math.Max(1, effect.Duration);
        var hours = 0;
        for (var die = 0; die < diceCount; die++)
        {
            hours = checked(hours + _dice.Roll(4));
        }

        return effect with { Duration = hours, DurationUnit = TraumaDurationUnit.Hours };
    }

    private bool HasLegalCleaveTarget(ObjectId attackerId, ObjectId firstTargetId)
    {
        var attacker = _objects.Get(attackerId);
        var first = _objects.Get(firstTargetId);
        if (attacker.Location.Kind != LocationKind.OnMap ||
            first.Location.Kind != LocationKind.OnMap ||
            attacker.Location.MapId != first.Location.MapId)
        {
            return false;
        }

        return _objects.Enumerate().Any(candidate =>
            candidate.Id != attackerId && candidate.Id != firstTargetId &&
            candidate.HasFlag(ObjectFlags.Actor) && candidate.IsAlive &&
            candidate.Location.Kind == LocationKind.OnMap &&
            candidate.Location.MapId == first.Location.MapId &&
            TileDistance(candidate, first) <= 1 &&
            ChebyshevDistance(candidate, attacker) <= 1);
    }

    private static int TileDistance(WorldObject left, WorldObject right) =>
        (Math.Abs(left.Location.Position.X - right.Location.Position.X) +
         Math.Abs(left.Location.Position.Y - right.Location.Position.Y)) /
        WorldMap.WorldUnitsPerTile;

    private static int ChebyshevDistance(WorldObject left, WorldObject right) =>
        Math.Max(
            Math.Abs(left.Location.Position.X - right.Location.Position.X),
            Math.Abs(left.Location.Position.Y - right.Location.Position.Y)) /
        WorldMap.WorldUnitsPerTile;

    private void PlanCorpseTransfers(
        WorldObject body,
        List<ObjectTransferRequest> transfers)
    {
        var alreadyMoved = transfers.Select(transfer => transfer.ObjectId).ToHashSet();
        var into = ObjectLocation.InContainer(body.Id);
        var carried = _objects.GetContents(body.Id)
            .Select(_objects.Get)
            .ToList();
        foreach (var item in _objects.Enumerate()
                     .Where(value => value.Location.Kind == LocationKind.Equipped &&
                                     value.Location.Parent == body.Id &&
                                     !alreadyMoved.Contains(value.Id))
                     .OrderBy(value => value.Id))
        {
            var projected = carried.Append(item with { Location = into }).ToArray();
            var destination = GearSlots.UsedBy(projected) <= body.CarryCapacity
                ? into
                : body.Location;
            transfers.Add(new ObjectTransferRequest(item.Id, item.Location, destination));
            if (destination == into)
            {
                carried.Add(item with { Location = into });
            }
        }
    }

    private InjuryState DeadState(ObjectId targetId, InjuryState? basis)
    {
        var injury = basis ?? _objects.InjuryOf(targetId);
        if (injury.IsDead)
        {
            return injury;
        }

        return injury with
        {
            Concussion = 0,
            Wounds = 0,
            State = VitalityState.Dead,
        };
    }

    private InjuryState BodyState(
        ObjectId targetId,
        VitalityState state,
        InjuryState? basis)
    {
        var injury = basis ?? _objects.InjuryOf(targetId);
        if (injury.IsDead)
        {
            return injury;
        }

        return injury with
        {
            Concussion = 0,
            Wounds = 0,
            DeathSaveSuccesses = 0,
            DeathSaveFailures = 0,
            State = injury.MaximumWounds < 1 && state == VitalityState.Dying
                ? VitalityState.Dead
                : state,
        };
    }

    private (ObjectTransferRequest Transfer, ObjectPhysicsUpdate Physics)? PlanPushAway(
        ObjectId attackerId,
        ObjectId targetId,
        int distanceFeet)
    {
        var attacker = _objects.Get(attackerId);
        var target = _objects.Get(targetId);
        if (attacker.Location.Kind != LocationKind.OnMap ||
            target.Location.Kind != LocationKind.OnMap ||
            attacker.Location.MapId != target.Location.MapId)
        {
            return null;
        }

        var dx = Math.Sign(target.Location.Position.X - attacker.Location.Position.X);
        var dy = Math.Sign(target.Location.Position.Y - attacker.Location.Position.Y);
        if (dx == 0 && dy == 0)
        {
            return null;
        }

        var tiles = Math.Max(1, (distanceFeet + 4) / 5);
        var resolution = _movement.Resolve(
            targetId,
            new Vec3i(
                dx * tiles * WorldMap.WorldUnitsPerTile,
                dy * tiles * WorldMap.WorldUnitsPerTile,
                0));
        if (!resolution.Moved)
        {
            return null;
        }

        var request = new ObjectTransferRequest(
            targetId,
            target.Location,
            ObjectLocation.OnMap(target.Location.MapId, resolution.ResolvedPosition));
        var physics = new ObjectPhysicsUpdate(
            targetId,
            resolution.Motion,
            0,
            resolution.Support);
        return _transfers.ValidateForCommit([request]).Succeeded
            ? (request, physics)
            : null;
    }

    private IReadOnlyList<ObjectTransferRequest> PlanDropHeld(
        ObjectId targetId,
        string? detail)
    {
        var target = _objects.Get(targetId);
        if (target.Location.Kind != LocationKind.OnMap)
        {
            return [];
        }

        var held = EquippedHands(targetId)
            .Where(item => Matches(item, detail))
            .ToArray();
        if (held.Length == 0 && detail is not null)
        {
            held = EquippedHands(targetId).Take(1).ToArray();
        }

        var dropped = new List<ObjectTransferRequest>();
        foreach (var item in held)
        {
            dropped.Add(new ObjectTransferRequest(
                item.Id, item.Location, target.Location));
        }

        return dropped;
    }

    private IReadOnlyList<ObjectId> PlanBreakEquipped(ObjectId targetId, string? detail)
    {
        var candidates = _objects.Enumerate()
            .Where(item => item.Location.Kind == LocationKind.Equipped &&
                           item.Location.Parent == targetId &&
                           Matches(item, detail))
            .OrderBy(item => item.Id)
            .ToArray();
        if (candidates.Length == 0)
        {
            candidates = EquippedHands(targetId).Take(1).ToArray();
        }

        return candidates.Select(item => item.Id).ToArray();
    }

    private IEnumerable<WorldObject> EquippedHands(ObjectId actorId) =>
        _objects.Enumerate()
            .Where(item => item.Location.Kind == LocationKind.Equipped &&
                           item.Location.Parent == actorId &&
                           item.Location.Slot is (byte)EquipmentSlot.RightHand or
                               (byte)EquipmentSlot.LeftHand)
            .OrderBy(item => item.Location.Slot)
            .ThenBy(item => item.Id);

    private static bool Matches(WorldObject item, string? detail) =>
        detail is null ||
        item.Name.Contains(detail, StringComparison.OrdinalIgnoreCase) ||
        (detail.Contains("shield", StringComparison.OrdinalIgnoreCase) &&
         item.DefenseBonus > 0);
}

using Ash.Rules;

namespace Ash.Sim;

/// <summary>The replaceable rules boundary used by simulation combat.</summary>
public interface IAttackRulesResolver
{
    AttackResult Resolve(AttackRequest request);
}

/// <summary>Resolves attacks with the currently loaded authored rules.</summary>
public sealed class RulesAttackRulesResolver : IAttackRulesResolver
{
    public AttackResult Resolve(AttackRequest request) =>
        AttackResolver.Resolve(RulesRepository.Rules, request);
}

/// <summary>The complete result of one melee decision and its immediate harm.</summary>
public sealed record CombatAttackOutcome(
    ObjectId AttackerId,
    ObjectId TargetId,
    AttackProfile Profile,
    AttackRequest Request,
    AttackResult Result,
    IReadOnlyList<TraumaEffect> ApplicableTraumaEffects,
    int ImmediateHits,
    DamageOutcome? Damage,
    TraumaDispatchResult Trauma,
    InjuryState FinalTargetState)
{
    public bool Hit => Result.Hit;
}

/// <summary>
/// The sole simulation path from an adjacent attack decision to rules damage.
/// Lasting trauma is deliberately returned, rather than persisted, until the
/// condition-state phase gives it a durable home.
/// </summary>
public sealed class CombatAttackService
{
    private readonly ObjectStore _objects;
    private readonly ActorSheets _sheets;
    private readonly ActorVitality _vitality;
    private readonly Dice _dice;
    private readonly IAttackRulesResolver _resolver;
    private readonly ActorConditionService? _conditions;
    private readonly Func<long>? _currentTick;
    private readonly TraumaEffectDispatcher? _trauma;
    private readonly Action<CombatMutationStage>? _afterMutationStage;

    public CombatAttackService(
        ObjectStore objects,
        ActorSheets sheets,
        ActorVitality vitality,
        Dice dice,
        IAttackRulesResolver resolver,
        ActorConditionService? conditions = null,
        Func<long>? currentTick = null,
        TraumaEffectDispatcher? trauma = null,
        Action<CombatMutationStage>? afterMutationStage = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        _vitality = vitality ?? throw new ArgumentNullException(nameof(vitality));
        _dice = dice ?? throw new ArgumentNullException(nameof(dice));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _conditions = conditions;
        _currentTick = currentTick;
        _trauma = trauma;
        _afterMutationStage = afterMutationStage;
    }

    public CombatAttackOutcome ResolveMelee(ObjectId attackerId, ObjectId targetId)
    {
        var attacker = LivingActor(attackerId, nameof(attackerId));
        var target = LivingActor(targetId, nameof(targetId));
        if (_conditions?.PreventsAction(attackerId) == true)
        {
            throw new InvalidOperationException($"{attacker.Name} cannot attack under its current conditions.");
        }
        var attackerSheet = _sheets.For(attackerId);
        var targetSheet = _sheets.For(targetId);
        var profile = attackerSheet.AttackProfile ?? throw new InvalidOperationException(
            $"{attacker.Name} has no attack profile for its equipped weapon.");
        RequireMeleeReach(attacker, target, profile, IsLegalCleaveFollowUp(attackerId, targetId));

        var diceRollback = _dice.State;
        var conditionRollback = _conditions?.Capture();
        try
        {
            var consumptions = new List<ActorConditionConsumption>();
            if (IsLegalCleaveFollowUp(attackerId, targetId))
            {
                var cleave = _conditions!.Of(attackerId)
                    .Where(condition => condition.Kind == TraumaEffectKind.Cleave &&
                                        condition.SourceId != targetId)
                    .OrderBy(condition => condition.AppliedTick)
                    .ThenBy(condition => condition.SourceId)
                    .First();
                consumptions.Add(new ActorConditionConsumption(
                    attackerId,
                    TraumaEffectKind.Cleave,
                    cleave.SourceId));
            }

            var rawD20 = _dice.D20();
            if (_conditions?.Has(attackerId, TraumaEffectKind.Sap) == true)
            {
                rawD20 = Math.Min(rawD20, _dice.D20());
                consumptions.Add(new ActorConditionConsumption(
                    attackerId,
                    TraumaEffectKind.Sap));
            }

            if (_conditions?.Has(targetId, TraumaEffectKind.Vex, attackerId) == true)
            {
                rawD20 = Math.Max(rawD20, _dice.D20());
                consumptions.Add(new ActorConditionConsumption(
                    targetId,
                    TraumaEffectKind.Vex,
                    attackerId));
            }

            var attackPenalty = ConditionPenalty(attackerId);
            var defensePenalty = ConditionPenalty(targetId);
            var request = new AttackRequest(
                rawD20,
                profile.Category,
                checked(attackerSheet.AttackModifier - attackPenalty),
                checked(targetSheet.DefenseModifier - defensePenalty),
                targetSheet.Armor,
                profile.CriticalTable,
                AttackSize: profile.Size,
                MaximumCriticalTier: profile.MaximumCriticalTier);
            var result = _resolver.Resolve(request);
            var governingModifier = Math.Max(0, RulesRepository.AbilityBonuses.BonusOf(
                attackerSheet.Abilities,
                attackerSheet.GoverningAbility));
            var applicable = result.TraumaEffects
                .Where(effect => Applies(effect.AppliesWhen, targetId, targetSheet))
                .Select(effect => effect.Kind == TraumaEffectKind.Graze
                    ? effect with
                    {
                        Magnitude = checked(
                            Math.Max(1, effect.Magnitude) * governingModifier),
                    }
                    : effect)
                .ToArray();
            var immediateHits = result.Hit
                ? checked(result.ConcussionHits + applicable
                    .Where(effect => effect.Kind == TraumaEffectKind.AdditionalHits)
                    .Sum(effect => effect.Magnitude))
                : applicable
                    .Where(effect => effect.Kind == TraumaEffectKind.Graze)
                    .Sum(effect => Math.Max(0, effect.Magnitude));
            DamageOutcome? damage = null;
            if (immediateHits > 0)
            {
                damage = _vitality.ResolveDamage(targetId, immediateHits);
            }

            TraumaDispatchResult trauma = default;
            if ((result.Hit || immediateHits > 0) && _trauma is not null)
            {
                trauma = _trauma.Apply(
                    attackerId,
                    targetId,
                    applicable,
                    damage?.State,
                    consumptions,
                    _afterMutationStage);
            }
            else
            {
                var conditionMutations = result.Hit && _conditions is not null
                    ? applicable
                        .Where(effect => ActorConditionService.CanStore(effect.Kind))
                        .Select(effect => new ActorConditionMutation(
                            targetId,
                            attackerId,
                            effect,
                            _currentTick?.Invoke() ?? 0))
                        .ToArray()
                    : [];
                if (damage is not null || conditionMutations.Length > 0 ||
                    consumptions.Count > 0)
                {
                    _objects.CommitCombatMutation(
                        new CombatMutation(
                            targetId,
                            damage?.State,
                            Conditions: conditionMutations,
                            ConditionConsumptions: consumptions),
                        _conditions,
                        _afterMutationStage);
                }
            }

            return new CombatAttackOutcome(
                attackerId,
                targetId,
                profile,
                request,
                result,
                applicable,
                immediateHits,
                damage,
                trauma,
                _objects.InjuryOf(targetId));
        }
        catch
        {
            _dice.RestoreState(diceRollback);
            if (_conditions is not null && conditionRollback is not null)
            {
                _conditions.Restore(conditionRollback);
            }

            throw;
        }
    }

    public ObjectId WeaponOf(ObjectId actorId) => _sheets.For(actorId).Weapon;

    public int MeleeRangeOf(ObjectId actorId) =>
        _sheets.For(actorId).AttackProfile?.MeleeRangeTiles ?? 1;

    /// <summary>
    /// A Cleave token names the first target. Its one follow-up must choose a
    /// different living creature adjacent to that target and within the
    /// attacker's diagonal melee reach.
    /// </summary>
    public bool IsLegalCleaveFollowUp(ObjectId attackerId, ObjectId targetId)
    {
        if (_conditions is null ||
            !_objects.TryGet(attackerId, out var attacker) ||
            !_objects.TryGet(targetId, out var target) ||
            !target.HasFlag(ObjectFlags.Actor) || !target.IsAlive ||
            attacker.Location.Kind != LocationKind.OnMap ||
            target.Location.Kind != LocationKind.OnMap ||
            attacker.Location.MapId != target.Location.MapId)
        {
            return false;
        }

        return _conditions.Of(attackerId)
            .Where(condition => condition.Kind == TraumaEffectKind.Cleave &&
                                condition.SourceId != targetId)
            .Any(condition =>
                _objects.TryGet(condition.SourceId, out var first) &&
                first.Location.Kind == LocationKind.OnMap &&
                first.Location.MapId == target.Location.MapId &&
                TileDistance(first, target) <= 1 &&
                ChebyshevDistance(attacker, target) <= MeleeRangeOf(attackerId));
    }

    private int ConditionPenalty(ObjectId actorId) => _conditions?.Of(actorId)
        .Where(condition => condition.Kind is TraumaEffectKind.ActivityPenalty or
            TraumaEffectKind.Exhaustion or TraumaEffectKind.Injured or
            TraumaEffectKind.BreakBone or TraumaEffectKind.DisableLimb or
            TraumaEffectKind.DestroyEye)
        .Sum(condition => condition.Kind switch
        {
            TraumaEffectKind.Exhaustion or TraumaEffectKind.Injured =>
                2 * Math.Max(1, condition.Magnitude),
            TraumaEffectKind.DisableLimb => 4,
            TraumaEffectKind.DestroyEye => 2 * Math.Max(1, condition.Magnitude),
            _ => Math.Max(1, condition.Magnitude),
        }) ?? 0;

    private WorldObject LivingActor(ObjectId id, string parameter)
    {
        var actor = _objects.Get(id);
        if (!actor.HasFlag(ObjectFlags.Actor) || !actor.IsAlive || !actor.Injury.IsUpright)
        {
            throw new InvalidOperationException($"{parameter} must identify an upright living actor.");
        }

        return actor;
    }

    private static void RequireMeleeReach(
        WorldObject attacker,
        WorldObject target,
        AttackProfile profile,
        bool legalCleaveFollowUp)
    {
        if (attacker.Location.Kind != LocationKind.OnMap ||
            target.Location.Kind != LocationKind.OnMap ||
            attacker.Location.MapId != target.Location.MapId)
        {
            throw new InvalidOperationException("Melee attackers must stand on the same map.");
        }

        var distance = (Math.Abs(attacker.Location.Position.X - target.Location.Position.X) +
                        Math.Abs(attacker.Location.Position.Y - target.Location.Position.Y)) /
                       WorldMap.WorldUnitsPerTile;
        if (distance > profile.MeleeRangeTiles && !legalCleaveFollowUp)
        {
            throw new InvalidOperationException("Target is outside melee reach.");
        }
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

    private bool Applies(
        TraumaEffectCondition condition,
        ObjectId targetId,
        ActorSheet targetSheet) => condition switch
    {
        TraumaEffectCondition.Always => true,
        TraumaEffectCondition.NoShield => targetSheet.ShieldModifier == 0,
        TraumaEffectCondition.NoHelm => !Wearing(targetId, EquipmentSlot.Head),
        TraumaEffectCondition.NoArmArmor =>
            !Wearing(targetId, EquipmentSlot.Body) && !Wearing(targetId, EquipmentSlot.Gloves),
        TraumaEffectCondition.NoLegArmor =>
            !Wearing(targetId, EquipmentSlot.Body) && !Wearing(targetId, EquipmentSlot.Boots),
        _ => throw new ArgumentOutOfRangeException(nameof(condition)),
    };

    private bool Wearing(ObjectId actorId, EquipmentSlot slot) => _objects.Enumerate().Any(value =>
        value.Location.Kind == LocationKind.Equipped &&
        value.Location.Parent == actorId &&
        value.Location.Slot == (byte)slot &&
        !value.HasFlag(ObjectFlags.Broken));
}

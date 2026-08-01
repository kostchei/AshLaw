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

    public CombatAttackService(
        ObjectStore objects,
        ActorSheets sheets,
        ActorVitality vitality,
        Dice dice,
        IAttackRulesResolver resolver,
        ActorConditionService? conditions = null,
        Func<long>? currentTick = null,
        TraumaEffectDispatcher? trauma = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        _vitality = vitality ?? throw new ArgumentNullException(nameof(vitality));
        _dice = dice ?? throw new ArgumentNullException(nameof(dice));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _conditions = conditions;
        _currentTick = currentTick;
        _trauma = trauma;
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
        RequireMeleeReach(attacker, target, profile);

        var rawD20 = _dice.D20();
        if (_conditions is not null &&
            _conditions.Consume(attackerId, TraumaEffectKind.Sap))
        {
            rawD20 = Math.Min(rawD20, _dice.D20());
        }

        if (_conditions is not null &&
            _conditions.Consume(targetId, TraumaEffectKind.Vex, attackerId))
        {
            rawD20 = Math.Max(rawD20, _dice.D20());
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
        var applicable = result.TraumaEffects
            .Where(effect => Applies(effect.AppliesWhen, targetId, targetSheet))
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
            var resolvedDamage = _vitality.ResolveDamage(targetId, immediateHits);
            damage = resolvedDamage;
        }
        TraumaDispatchResult trauma = default;
        if ((result.Hit || immediateHits > 0) && _trauma is not null)
        {
            trauma = _trauma.Apply(
                attackerId,
                targetId,
                applicable,
                damage?.State);
        }
        else
        {
            if (damage is not null)
            {
                _objects.CommitCombatMutation(
                    new CombatMutation(targetId, damage.Value.State));
            }

            if (result.Hit && _conditions is not null)
            {
                foreach (var effect in applicable.Where(effect =>
                             ActorConditionService.CanStore(effect.Kind)))
                {
                    _conditions.Apply(
                        targetId, attackerId, effect, _currentTick?.Invoke() ?? 0);
                }
            }
        }
        return new CombatAttackOutcome(
            attackerId, targetId, profile, request, result, applicable, immediateHits, damage,
            trauma, _objects.InjuryOf(targetId));
    }

    public ObjectId WeaponOf(ObjectId actorId) => _sheets.For(actorId).Weapon;

    public int MeleeRangeOf(ObjectId actorId) =>
        _sheets.For(actorId).AttackProfile?.MeleeRangeTiles ?? 1;

    private int ConditionPenalty(ObjectId actorId) => _conditions?.Of(actorId)
        .Where(condition => condition.Kind is TraumaEffectKind.ActivityPenalty or
            TraumaEffectKind.Exhaustion or TraumaEffectKind.Injured)
        .Sum(condition => Math.Max(1, condition.Magnitude)) ?? 0;

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
        AttackProfile profile)
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
        if (distance > profile.MeleeRangeTiles)
        {
            throw new InvalidOperationException("Target is outside melee reach.");
        }
    }

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

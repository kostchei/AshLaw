using Ash.Rules;

namespace Ash.Sim;

/// <summary>
/// What a new body starts with: its rolled concussion hits and its wound pool.
/// </summary>
public readonly record struct RolledBody(
    ConcussionHitPool Concussion,
    int MaximumWounds)
{
    public int MaximumConcussion => Concussion.Maximum;
}

/// <summary>
/// The seam between the object store's numbers and the injury rules' meaning.
/// </summary>
/// <remarks>
/// <para>
/// Nothing else in the simulation subtracts from a body. Damage, death saves,
/// healing and a day's rest all resolve in <see cref="Injury"/> as pure
/// functions of the state and the dice, and land back in the store as one
/// assignment through <see cref="ObjectStore.SetInjury"/>. That is what keeps
/// concussion hits, wounds, the death clock and impairment from disagreeing.
/// </para>
/// <para>
/// The dice are the world's, not this service's: the same seed and the same
/// sequence of blows must produce the same corpse, and a saved world resumes
/// the rolls it would have made.
/// </para>
/// </remarks>
public sealed class ActorVitality
{
    private readonly ObjectStore _objects;
    private readonly VitalityData _data;
    private readonly AbilityBonusTable _bonuses;
    private readonly Dice _dice;

    public ActorVitality(
        ObjectStore objects,
        VitalityData data,
        AbilityBonusTable bonuses,
        Dice dice)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _bonuses = bonuses ?? throw new ArgumentNullException(nameof(bonuses));
        _dice = dice ?? throw new ArgumentNullException(nameof(dice));
    }

    /// <summary>
    /// Rolls the body a new character walks in with: one hit die per level
    /// carrying the constitution bonus, over a wound pool of level plus
    /// whatever they are best at.
    /// </summary>
    public static RolledBody RollBody(
        VitalityData data,
        AbilityBonusTable bonuses,
        Dice dice,
        CharacterClass characterClass,
        int level,
        AbilityScores scores) =>
        new(
            Vitality.RollConcussionHits(
                data,
                dice,
                characterClass,
                level,
                scores,
                bonuses),
            Vitality.MaximumWounds(data, level, scores, bonuses));

    public RolledBody RollBody(
        CharacterClass characterClass,
        int level,
        AbilityScores scores) =>
        RollBody(_data, _bonuses, _dice, characterClass, level, scores);

    public InjuryState Of(ObjectId actorId) => _objects.InjuryOf(actorId);

    /// <summary>
    /// Hurts a body: concussion hits first, then wounds, then the death clock.
    /// </summary>
    public DamageOutcome Damage(ObjectId actorId, int amount)
    {
        var outcome = Injury.Damage(_objects.InjuryOf(actorId), amount, _dice);
        _objects.SetInjury(actorId, outcome.State);
        return outcome;
    }

    /// <summary>One round of the death clock, for a body that is on it.</summary>
    public DeathSaveOutcome RollDeathSave(ObjectId actorId)
    {
        var outcome = Injury.RollDeathSave(
            _data,
            _objects.InjuryOf(actorId),
            _dice,
            _objects.Get(actorId).Abilities,
            _bonuses);
        _objects.SetInjury(actorId, outcome.State);
        return outcome;
    }

    /// <summary>A potion or a healing spell: a wound first, then the rest.</summary>
    public HealOutcome Heal(ObjectId actorId, int amount)
    {
        var outcome = Injury.Heal(_data, _objects.InjuryOf(actorId), amount);
        _objects.SetInjury(actorId, outcome.State);
        return outcome;
    }

    /// <summary>A day's recovery, and the complication it might bring.</summary>
    public DayOutcome PassDay(
        ObjectId actorId,
        CareLevel care,
        bool fullRest,
        bool medicalAttention)
    {
        var actor = _objects.Get(actorId);
        var outcome = Injury.PassDay(
            _data,
            _objects.InjuryOf(actorId),
            _dice,
            actor.Class,
            actor.Level,
            actor.Abilities,
            _bonuses,
            care,
            fullRest,
            medicalAttention);
        _objects.SetInjury(actorId, outcome.State);
        return outcome;
    }
}

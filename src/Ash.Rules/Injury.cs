namespace Ash.Rules;

/// <summary>Where a body stands between unhurt and gone.</summary>
public enum VitalityState
{
    /// <summary>Upright: concussion hits or wounds left.</summary>
    Standing = 0,

    /// <summary>Out of wounds and on the death clock, saving every round.</summary>
    Dying = 1,

    /// <summary>Came off the clock alive, and is out of wounds until healed.</summary>
    Stable = 2,

    /// <summary>The clock ran out.</summary>
    Dead = 3,
}

/// <summary>How a round of the death clock ended.</summary>
public enum DeathClockOutcome : byte
{
    /// <summary>The clock is still running.</summary>
    Continuing = 0,

    /// <summary>Enough successes: alive, and paying for it.</summary>
    Stabilised = 1,

    /// <summary>Enough failures.</summary>
    Died = 2,
}

/// <summary>
/// Everything about how hurt a body is, as one value.
/// </summary>
/// <remarks>
/// This is a reading of authoritative state, not a cache: the store holds these
/// numbers on the object and every transition here is pure, so the same state
/// and the same dice always produce the same outcome.
/// </remarks>
public readonly record struct InjuryState(
    int Concussion,
    int MaximumConcussion,
    int Wounds,
    int MaximumWounds,
    int DeathSaveSuccesses,
    int DeathSaveFailures,
    AbilityMask Impairments,
    VitalityState State)
{
    /// <summary>A body at full health, which is where a new character starts.</summary>
    public static InjuryState Whole(int maximumConcussion, int maximumWounds) =>
        new(
            maximumConcussion,
            maximumConcussion,
            maximumWounds,
            maximumWounds,
            0,
            0,
            AbilityMask.None,
            VitalityState.Standing);

    public bool IsDead => State == VitalityState.Dead;

    public bool IsOnTheDeathClock => State == VitalityState.Dying;

    /// <summary>Whether the body can still act.</summary>
    public bool IsUpright => State == VitalityState.Standing;

    public int ConcussionMissing => MaximumConcussion - Concussion;

    public int WoundsMissing => MaximumWounds - Wounds;
}

public readonly record struct DamageOutcome(
    InjuryState State,
    int ConcussionLost,
    int WoundsLost,
    AbilityMask NewImpairments,
    bool EnteredDeathClock);

public readonly record struct DeathSaveOutcome(
    InjuryState State,
    AbilityCheckResult Check,
    bool Succeeded,
    DeathClockOutcome Outcome,
    AbilityMask NewImpairments);

public readonly record struct HealOutcome(
    InjuryState State,
    int WoundsRestored,
    int ConcussionRestored,
    bool ClearedImpairments);

public readonly record struct DayOutcome(
    InjuryState State,
    int WoundsRestored,
    IReadOnlyList<HitDieResult> RecoveryDice,
    int ConcussionRestored,
    AbilityCheckResult? InfectionSave,
    int InfectionDamage,
    bool ClearedImpairments);

/// <summary>
/// The injury model: what damage does, what the death clock does, and what a
/// day of rest gives back.
/// </summary>
/// <remarks>
/// <para>
/// Damage spends concussion hits first. What is left over spends wounds, one
/// for one, and every wound costs the reliable use of an ability chosen at
/// random — the body still knows how, but no longer does it on demand. Running
/// out of wounds does not kill anyone directly: it starts the death clock, a
/// constitution save every round until enough successes or enough failures.
/// </para>
/// <para>
/// Because a lost wound can impair constitution, the death clock can be rolled
/// at disadvantage by the very injury that started it. That is the intended
/// reading of "disadvantage to checks with a random stat", not an accident of
/// the implementation.
/// </para>
/// <para>
/// Every function here is pure. Nothing reads a clock, a store or an ambient
/// generator; callers pass the dice and take the new state back.
/// </para>
/// </remarks>
public static class Injury
{
    /// <summary>
    /// Applies damage. Concussion hits absorb what they can, the rest cuts into
    /// wounds, and reaching no wounds starts the death clock rather than ending
    /// the character.
    /// </summary>
    /// <remarks>
    /// Damage taken while already out of wounds puts a stabilised body back on
    /// the clock, carrying the successes and failures it already has: a body
    /// that has failed twice does not get a fresh start for being hit again.
    /// </remarks>
    public static DamageOutcome Damage(InjuryState state, int amount, Dice dice)
    {
        ArgumentNullException.ThrowIfNull(dice);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Damage is a positive quantity; healing is Heal.");
        }

        if (state.IsDead)
        {
            throw new InvalidOperationException(
                "A dead body cannot take damage; the caller is holding a stale " +
                "injury state.");
        }

        var concussionLost = Math.Min(state.Concussion, amount);
        var overflow = amount - concussionLost;
        var woundsLost = Math.Min(state.Wounds, overflow);
        var impairments = state.Impairments;
        var added = AbilityMask.None;
        for (var wound = 0; wound < woundsLost; wound++)
        {
            var ability = RollAbility(dice);
            added |= ability.AsMask();
            impairments |= ability.AsMask();
        }

        var concussion = state.Concussion - concussionLost;
        var wounds = state.Wounds - woundsLost;
        var wasOnTheClock = state.State is VitalityState.Dying;

        // A body with no wound layer has nothing between its last concussion
        // hit and the end. The death clock is what the wound layer buys, so a
        // creature that was never given one dies where a character would start
        // saving.
        var emptied = state.MaximumWounds < 1
            ? VitalityState.Dead
            : VitalityState.Dying;
        var next = state with
        {
            Concussion = concussion,
            Wounds = wounds,
            Impairments = impairments,
            State = concussion > 0 || wounds > 0
                ? VitalityState.Standing
                : emptied,
        };
        return new DamageOutcome(
            next,
            concussionLost,
            woundsLost,
            added,
            next.State == VitalityState.Dying && !wasOnTheClock);
    }

    /// <summary>
    /// One round of the death clock: a constitution save, at disadvantage if
    /// the injury that started the clock took constitution with it.
    /// </summary>
    public static DeathSaveOutcome RollDeathSave(
        VitalityData data,
        InjuryState state,
        Dice dice,
        AbilityScores scores,
        AbilityBonusTable bonuses)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(bonuses);
        if (!state.IsOnTheDeathClock)
        {
            throw new InvalidOperationException(
                $"A {state.State} body is not on the death clock.");
        }

        var rules = data.DeathClock;
        var check = AbilityCheck.Against(
            dice,
            scores,
            bonuses,
            Ability.Constitution,
            state.Impairments);
        var succeeded = check.Beats(rules.SaveDc);
        var successes = state.DeathSaveSuccesses + (succeeded ? 1 : 0);
        var failures = state.DeathSaveFailures + (succeeded ? 0 : 1);
        var next = state with
        {
            DeathSaveSuccesses = successes,
            DeathSaveFailures = failures,
        };

        if (failures >= rules.FailuresToDie)
        {
            return new DeathSaveOutcome(
                next with { State = VitalityState.Dead },
                check,
                succeeded,
                DeathClockOutcome.Died,
                AbilityMask.None);
        }

        if (successes < rules.SuccessesToStabilise)
        {
            return new DeathSaveOutcome(
                next,
                check,
                succeeded,
                DeathClockOutcome.Continuing,
                AbilityMask.None);
        }

        // Surviving the clock is not recovery. The body comes off it stable and
        // still out of wounds, carrying two more abilities' worth of damage
        // until something heals it.
        var impairments = state.Impairments;
        var added = AbilityMask.None;
        for (var index = 0;
             index < data.Wounds.ImpairmentsOnDeathClockSurvival;
             index++)
        {
            var ability = RollAbility(dice);
            added |= ability.AsMask();
            impairments |= ability.AsMask();
        }

        return new DeathSaveOutcome(
            next with { State = VitalityState.Stable, Impairments = impairments },
            check,
            succeeded,
            DeathClockOutcome.Stabilised,
            added);
    }

    /// <summary>
    /// A potion or a healing spell. It restores wounds first — the configured
    /// one, normally — and spends everything left on concussion hits.
    /// </summary>
    /// <remarks>
    /// Healing takes a body off the death clock the moment it puts a wound
    /// back, and clears the death saves with it: the clock only counts while it
    /// is running.
    /// </remarks>
    public static HealOutcome Heal(VitalityData data, InjuryState state, int amount)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Healing is a positive quantity.");
        }

        if (state.IsDead)
        {
            throw new InvalidOperationException("Healing does not raise the dead.");
        }

        var woundsRestored = Math.Min(
            Math.Min(data.WoundRecovery.PerHealing, state.WoundsMissing),
            amount);
        var concussionRestored = Math.Min(
            amount - woundsRestored,
            state.ConcussionMissing);
        return Restore(state, woundsRestored, concussionRestored);
    }

    /// <summary>
    /// A day, and what it gives back: a wound, then as many hit dice as the
    /// standard of care offers, capped by level and by the dice the character
    /// has. Any wound still missing at the start of the day risks a
    /// complication — a constitution save against the standard of care, and a
    /// failure costs concussion hits.
    /// </summary>
    /// <remarks>
    /// The infection save is judged on the wounds the character carried
    /// <em>through</em> the day, not on what is left after the day's healing:
    /// a wound that closes overnight was still open while it could go wrong.
    /// </remarks>
    public static DayOutcome PassDay(
        VitalityData data,
        InjuryState state,
        Dice dice,
        CharacterClass characterClass,
        int level,
        AbilityScores scores,
        AbilityBonusTable bonuses,
        CareLevel care,
        bool fullRest,
        bool medicalAttention)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(bonuses);
        if (state.IsDead || state.IsOnTheDeathClock)
        {
            throw new InvalidOperationException(
                $"A {state.State} body cannot rest a day out; the death clock " +
                "resolves in rounds.");
        }

        var wasWounded = state.WoundsMissing > 0;
        var woundsRestored = Math.Min(
            data.WoundRecovery.PerDay,
            state.WoundsMissing);

        var recovery = data.ConcussionRecovery;
        var diceSpent = Math.Min(
            Math.Min(
                recovery.OfferedDice(fullRest, medicalAttention),
                recovery.DailyCapAt(level)),
            data.HitDice.DiceAt(level));
        var bonusPerDie = Vitality.ConstitutionBonusPerDie(
            data,
            characterClass,
            scores,
            bonuses);

        // Every offered die is rolled even when the body is nearly whole, so a
        // day consumes the same number of rolls whatever state it starts in and
        // a saved world resumes the sequence it would have rolled.
        var rolled = new HitDieResult[diceSpent];
        var recovered = 0;
        for (var index = 0; index < diceSpent; index++)
        {
            rolled[index] = Vitality.RollHitDie(
                data,
                dice,
                characterClass,
                bonusPerDie);
            recovered = checked(recovered + rolled[index].Hits);
        }

        var afterWound = Restore(state, woundsRestored, 0);
        var concussionRestored = Math.Min(
            recovered,
            afterWound.State.ConcussionMissing);
        var healed = Restore(afterWound.State, 0, concussionRestored);
        var next = healed.State;
        var clearedImpairments =
            afterWound.ClearedImpairments || healed.ClearedImpairments;

        if (!wasWounded)
        {
            return new DayOutcome(
                next,
                woundsRestored,
                rolled,
                concussionRestored,
                null,
                0,
                clearedImpairments);
        }

        var save = AbilityCheck.Against(
            dice,
            scores,
            bonuses,
            Ability.Constitution,
            next.Impairments);
        if (save.Beats(data.Infection.DcFor(care)))
        {
            return new DayOutcome(
                next,
                woundsRestored,
                rolled,
                concussionRestored,
                save,
                0,
                clearedImpairments);
        }

        var damage = 0;
        for (var die = 0; die < data.Infection.DamageDice; die++)
        {
            damage = checked(damage + dice.Roll(data.Infection.DamageDieSides));
        }

        var complication = Damage(next, damage, dice);
        return new DayOutcome(
            complication.State,
            woundsRestored,
            rolled,
            concussionRestored,
            save,
            damage,
            clearedImpairments);
    }

    /// <summary>
    /// The one place healing is applied, so wounds, the death clock and
    /// impairment can never disagree about what "healed" means.
    /// </summary>
    private static HealOutcome Restore(
        InjuryState state,
        int wounds,
        int concussion)
    {
        var next = state with
        {
            Wounds = state.Wounds + wounds,
            Concussion = state.Concussion + concussion,
        };

        if (next.Wounds > 0 && state.State is VitalityState.Dying or VitalityState.Stable)
        {
            next = next with
            {
                State = VitalityState.Standing,
                DeathSaveSuccesses = 0,
                DeathSaveFailures = 0,
            };
        }

        // Impairment lasts "until healed", and a body with every wound back is
        // healed. Nothing partial lifts it: that is what makes the wound layer
        // worth avoiding rather than a second hit-point bar.
        var cleared = false;
        if (next.Wounds >= next.MaximumWounds &&
            next.Impairments != AbilityMask.None)
        {
            next = next with { Impairments = AbilityMask.None };
            cleared = true;
        }

        return new HealOutcome(next, wounds, concussion, cleared);
    }

    private static Ability RollAbility(Dice dice) =>
        (Ability)(dice.Roll(AbilityScores.AbilityCount) - 1);
}

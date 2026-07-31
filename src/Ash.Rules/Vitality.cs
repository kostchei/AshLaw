namespace Ash.Rules;

/// <summary>One hit die, and what it was worth after the constitution bonus.</summary>
/// <remarks>
/// The raw roll is kept beside the result so a log can say why a level gave
/// what it gave, and so a wizard's minimum-floored die is visibly floored
/// rather than silently improved.
/// </remarks>
public readonly record struct HitDieResult(
    int Roll,
    int ConstitutionBonus,
    int Hits);

/// <summary>A character's rolled concussion hits, die by die.</summary>
public sealed record ConcussionHitPool(
    int Maximum,
    int DieSides,
    int ConstitutionBonusPerDie,
    IReadOnlyList<HitDieResult> Dice);

/// <summary>
/// How much punishment a body holds: concussion hits over a layer of wounds.
/// </summary>
/// <remarks>
/// <para>
/// Concussion hits are the outer layer — the vendored rules produce them as a
/// damage quantity and leave where they land to the engine, which is here. They
/// are rolled, one hit die per level up to ten, and each die carries the
/// constitution bonus with it, so constitution is worth more to a body that has
/// lived longer. A fighter takes that bonus in full; every other class takes it
/// up to a cap.
/// </para>
/// <para>
/// Wounds are the layer underneath, and they are not rolled: level plus the
/// character's best ability bonus, whichever ability that turns out to be. A
/// character is defined by what they are good at, so that is what keeps them
/// alive once the hits are gone.
/// </para>
/// </remarks>
public static class Vitality
{
    /// <summary>
    /// The constitution bonus each of this character's hit dice carries: the
    /// full bonus for a fighter, the capped one for anybody else.
    /// </summary>
    public static int ConstitutionBonusPerDie(
        VitalityData data,
        CharacterClass characterClass,
        AbilityScores scores,
        AbilityBonusTable bonuses)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(bonuses);
        return data.HitDice.ConstitutionBonusFor(
            characterClass,
            bonuses.BonusOf(scores, Ability.Constitution));
    }

    /// <summary>
    /// One hit die: the roll plus the constitution bonus, floored so a die
    /// never takes concussion hits away from a frail character.
    /// </summary>
    public static HitDieResult RollHitDie(
        VitalityData data,
        Dice dice,
        CharacterClass characterClass,
        int constitutionBonusPerDie)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(dice);
        var sides = data.HitDice.For(characterClass).DieSides;
        var roll = dice.Roll(sides);
        return new HitDieResult(
            roll,
            constitutionBonusPerDie,
            Math.Max(
                checked(roll + constitutionBonusPerDie),
                data.HitDice.MinimumPerDie));
    }

    /// <summary>
    /// A character's whole concussion pool, rolled one die per level up to the
    /// ten a body ever accumulates.
    /// </summary>
    public static ConcussionHitPool RollConcussionHits(
        VitalityData data,
        Dice dice,
        CharacterClass characterClass,
        int level,
        AbilityScores scores,
        AbilityBonusTable bonuses)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(dice);
        var bonus = ConstitutionBonusPerDie(data, characterClass, scores, bonuses);
        var count = data.HitDice.DiceAt(level);
        var rolled = new HitDieResult[count];
        var maximum = 0;
        for (var index = 0; index < count; index++)
        {
            rolled[index] = RollHitDie(data, dice, characterClass, bonus);
            maximum = checked(maximum + rolled[index].Hits);
        }

        return new ConcussionHitPool(
            maximum,
            data.HitDice.For(characterClass).DieSides,
            bonus,
            rolled);
    }

    /// <summary>
    /// The ability a character is best at. Ties go to the earlier ability in
    /// canonical order, because the wound pool has to be reproducible and a
    /// coin toss is not.
    /// </summary>
    public static Ability BestAbility(
        AbilityScores scores,
        AbilityBonusTable bonuses)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(bonuses);
        var best = Ability.Strength;
        var bestBonus = bonuses.BonusOf(scores, best);
        foreach (var ability in Enum.GetValues<Ability>())
        {
            var bonus = bonuses.BonusOf(scores, ability);
            if (bonus > bestBonus)
            {
                best = ability;
                bestBonus = bonus;
            }
        }

        return best;
    }

    /// <summary>
    /// The wound pool: level plus the best ability bonus, never below the
    /// configured minimum — a character with nothing going for them still has
    /// something between their last concussion hit and the death clock.
    /// </summary>
    public static int MaximumWounds(
        VitalityData data,
        int level,
        AbilityScores scores,
        AbilityBonusTable bonuses)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(bonuses);
        if (level < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "A character is at least level one.");
        }

        var best = bonuses.BonusOf(scores, BestAbility(scores, bonuses));
        return Math.Max(checked(level + best), data.Wounds.Minimum);
    }
}

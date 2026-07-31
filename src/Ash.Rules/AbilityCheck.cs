namespace Ash.Rules;

/// <summary>One d20 check or save, and everything that went into it.</summary>
/// <remarks>
/// Every roll is kept in the order it was made, and <see cref="Roll"/> is the
/// one that counted, so a log can say why a save failed.
/// </remarks>
public readonly record struct AbilityCheckResult(
    IReadOnlyList<int> Rolls,
    int Roll,
    int Bonus,
    int Total,
    bool HadAdvantage,
    bool HadDisadvantage)
{
    public bool Beats(int dc) => Total >= dc;
}

/// <summary>
/// The one place a d20 check is rolled against an ability.
/// </summary>
/// <remarks>
/// Advantage and disadvantage cancel: a character with both rolls once, like a
/// character with neither. When exactly one applies, both dice are always
/// rolled and both are kept, because consuming a fixed number of rolls is what
/// lets a saved world resume the sequence it would have rolled.
/// </remarks>
public static class AbilityCheck
{
    public static AbilityCheckResult Roll(
        Dice dice,
        int bonus,
        bool advantage = false,
        bool disadvantage = false)
    {
        ArgumentNullException.ThrowIfNull(dice);
        if (advantage == disadvantage)
        {
            var single = dice.D20();
            return new AbilityCheckResult(
                [single],
                single,
                bonus,
                checked(single + bonus),
                false,
                false);
        }

        var rolls = new[] { dice.D20(), dice.D20() };
        var roll = advantage
            ? Math.Max(rolls[0], rolls[1])
            : Math.Min(rolls[0], rolls[1]);
        return new AbilityCheckResult(
            rolls,
            roll,
            bonus,
            checked(roll + bonus),
            advantage,
            disadvantage);
    }

    /// <summary>
    /// A check against one ability, taking disadvantage from impairment. This
    /// is what a lost wound actually costs: the stat still has its bonus, but
    /// the body no longer performs it reliably.
    /// </summary>
    public static AbilityCheckResult Against(
        Dice dice,
        AbilityScores scores,
        AbilityBonusTable bonuses,
        Ability ability,
        AbilityMask impairments,
        bool advantage = false)
    {
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(bonuses);
        return Roll(
            dice,
            bonuses.BonusOf(scores, ability),
            advantage,
            impairments.Includes(ability));
    }
}

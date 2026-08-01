namespace Ash.Rules;

/// <summary>What happened when a creature committed a step onto danger.</summary>
public readonly record struct HazardCrossing(
    AbilityCheckResult Check,
    int Difficulty,
    int Damage)
{
    public bool Avoided => Check.Beats(Difficulty);
}

/// <summary>
/// The first-tier cracked-floor hazard: quick feet avoid it; a failed save
/// costs 1d4 concussion hits. The caller owns terrain and applies the result.
/// </summary>
public static class EnvironmentalHazard
{
    public const int Difficulty = 12;
    public const int DamageDieSides = 4;

    public static HazardCrossing CrossCrackedFloor(
        Dice dice,
        AbilityScores scores,
        AbilityBonusTable bonuses,
        AbilityMask impairments = AbilityMask.None,
        int reflexBonus = 0)
    {
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(bonuses);
        var ability = AbilityCheck.Against(
            dice,
            scores,
            bonuses,
            Ability.Dexterity,
            impairments);
        if (reflexBonus != 0)
        {
            ability = ability with
            {
                Bonus = checked(ability.Bonus + reflexBonus),
                Total = checked(ability.Total + reflexBonus),
            };
        }

        return new HazardCrossing(
            ability,
            Difficulty,
            ability.Beats(Difficulty)
                ? 0
                : dice.Roll(DamageDieSides));
    }
}

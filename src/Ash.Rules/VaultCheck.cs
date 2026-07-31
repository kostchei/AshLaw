namespace Ash.Rules;

/// <summary>
/// One attempt to get over something, and the height it turned out to clear.
/// </summary>
/// <remarks>
/// The rolls are kept so a log can say why a vault failed. Under advantage
/// there are two, in the order they were rolled, and <see cref="Roll"/> is the
/// one that counted.
/// </remarks>
public readonly record struct VaultCheckResult(
    Ability Ability,
    bool HadAdvantage,
    bool HadDisadvantage,
    IReadOnlyList<int> Rolls,
    int Roll,
    int Bonus,
    int Check,
    int StepHeight);

/// <summary>
/// How high a creature can get itself over something, rolled per attempt.
/// </summary>
/// <remarks>
/// Vaulting is not a property of a body, it is something a body tries. Two
/// people of the same build clear different obstacles on different days, and
/// the same person does not clear the same crate every time — so the step
/// height is the result of a check rather than a number on a sheet.
///
/// The check is a d20 against the better of strength and dexterity, because
/// there are two honest ways over an obstacle: heave yourself, or be quick
/// about it. A thief rolls dexterity with advantage — getting over things is
/// the job.
///
/// The height is <c>(check × 2) + 6</c>, bounded to [8, 56]. Those bounds are
/// not arbitrary trimming: a check runs 1 to 25 across the d20 and the bonus
/// table's +5 ceiling, and 1 and 25 map to exactly 8 and 56. The clamp only
/// catches the negative checks a weak creature can roll.
/// </remarks>
public static class VaultCheck
{
    /// <summary>The lowest anything clears: a kerb, a bridge deck.</summary>
    public const int MinimumStepHeight = 8;

    /// <summary>The highest anything clears on the best roll it can make.</summary>
    public const int MaximumStepHeight = 56;

    private const int Multiplier = 2;
    private const int Offset = 6;

    /// <summary>The height a given check result clears.</summary>
    public static int StepHeightFor(int check) =>
        Math.Clamp(
            checked((check * Multiplier) + Offset),
            MinimumStepHeight,
            MaximumStepHeight);

    /// <summary>
    /// Which ability carries the attempt: the better of strength and dexterity
    /// by score. A tie goes to dexterity, so a thief keeps the advantage that
    /// is the point of being one.
    /// </summary>
    public static Ability GoverningAbility(AbilityScores scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        return scores.Strength > scores.Dexterity
            ? Ability.Strength
            : Ability.Dexterity;
    }

    /// <summary>
    /// Whether the attempt is rolled twice and the better kept. Thieves get
    /// this on dexterity only: a thief heaving at something with raw strength
    /// is just a person heaving at something.
    /// </summary>
    public static bool HasAdvantage(
        CharacterClass characterClass,
        Ability ability) =>
        characterClass == CharacterClass.Rogue &&
        ability == Ability.Dexterity;

    /// <summary>
    /// Rolls the attempt. A body that has lost the reliable use of the ability
    /// carrying it rolls at disadvantage — which is what a wound costs, and why
    /// a hurt character should think twice about the crate they cleared on the
    /// way in.
    /// </summary>
    public static VaultCheckResult Roll(
        Dice dice,
        AbilityScores scores,
        CharacterClass characterClass,
        AbilityBonusTable bonuses,
        AbilityMask impairments = AbilityMask.None)
    {
        ArgumentNullException.ThrowIfNull(dice);
        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(bonuses);

        var ability = GoverningAbility(scores);
        var check = AbilityCheck.Against(
            dice,
            scores,
            bonuses,
            ability,
            impairments,
            HasAdvantage(characterClass, ability));
        return new VaultCheckResult(
            ability,
            check.HadAdvantage,
            check.HadDisadvantage,
            check.Rolls,
            check.Roll,
            check.Bonus,
            check.Total,
            StepHeightFor(check.Total));
    }
}

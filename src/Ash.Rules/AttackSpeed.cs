namespace Ash.Rules;

/// <summary>
/// What a combatant brings to the question of how often they swing.
/// </summary>
/// <remarks>
/// Mastery is a property of the character, not of the weapon: a fighter picks
/// two weapon masteries at first level, so "is this a mastery weapon" can only
/// be answered by comparing the weapon in hand against that character's list.
/// The caller answers it; this type does not guess.
/// </remarks>
public readonly record struct AttackSpeedInputs(
    CharacterClass Class,
    int Level,
    bool WieldingMasteryWeapon);

/// <summary>
/// How many attacks a combatant gets in the six-second round.
/// </summary>
/// <remarks>
/// The round is the unit the design is written in: six seconds, one attack, and
/// movement is free inside it — you may move and attack, or move and cast, in
/// the same round. Extra attacks are exceptions to that base, and each one has
/// to be earned by a rule that says so.
///
/// Source: <c>vendor/ash-v1-rules/rules/class_progression_tables.md</c> §6.3,
/// which grants the fighter a "2nd attack with mastery" at level 7.
/// </remarks>
public static class AttackSpeed
{
    /// <summary>The round: six seconds.</summary>
    public const int RoundMilliseconds = 6000;

    /// <summary>Everyone's base: one attack in the round.</summary>
    public const int BaseAttacksPerRound = 1;

    /// <summary>The level a fighter's mastery weapon earns a second attack.</summary>
    public const int FighterSecondAttackLevel = 7;

    public static int AttacksPerRound(AttackSpeedInputs inputs)
    {
        if (inputs.Level < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputs),
                inputs.Level,
                "A level cannot be negative.");
        }

        // A fighter of seventh level swings a weapon they have mastered twice in
        // the round. Mastery is the whole condition: the same fighter with an
        // unmastered weapon still swings once.
        if (inputs.Class == CharacterClass.Fighter &&
            inputs.Level >= FighterSecondAttackLevel &&
            inputs.WieldingMasteryWeapon)
        {
            return 2;
        }

        return BaseAttacksPerRound;

        // Not yet derived here, because the rules do not yet state the number:
        // a weapon in each hand, a weapon with the nick property, and haste all
        // grant extra attacks. Each needs a rate before it can be resolved
        // rather than assumed, so none of them is silently folded into the base.
    }

    /// <summary>
    /// The gap between one swing and the next: the round divided by the attacks
    /// it holds. Rates that do not divide the round evenly are refused, because
    /// a remainder would drift a fight out of step with the combat clock.
    /// </summary>
    public static int SwingIntervalMilliseconds(AttackSpeedInputs inputs)
    {
        var attacks = AttacksPerRound(inputs);
        if (RoundMilliseconds % attacks != 0)
        {
            throw new RulesResolutionException(
                $"{attacks} attacks do not divide the {RoundMilliseconds} ms " +
                "round evenly.");
        }

        return RoundMilliseconds / attacks;
    }
}

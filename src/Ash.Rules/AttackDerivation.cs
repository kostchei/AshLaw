namespace Ash.Rules;

/// <summary>
/// How a character's class, level and weapon become the Attack Modifier the
/// resolver takes.
/// </summary>
/// <remarks>
/// Weapons are driven by strength. A finesse weapon — rapier, dagger, whip —
/// may be driven by dexterity instead, whichever serves the wielder better, so
/// choosing one is never a downgrade. A tie goes to strength, which keeps the
/// answer stable when both are equal.
/// </remarks>
public static class AttackDerivation
{
    /// <summary>The ability a weapon is swung with.</summary>
    public static Ability GoverningAbility(AbilityScores scores, bool finesse)
    {
        ArgumentNullException.ThrowIfNull(scores);
        return finesse &&
            scores.BonusOf(Ability.Dexterity) > scores.BonusOf(Ability.Strength)
            ? Ability.Dexterity
            : Ability.Strength;
    }

    /// <summary>What the wielder's body adds to the attack.</summary>
    public static int WeaponAbilityBonus(AbilityScores scores, bool finesse) =>
        scores.BonusOf(GoverningAbility(scores, finesse));

    /// <summary>
    /// The Attack Modifier: what the class has learned, what the body brings,
    /// and whatever the moment adds.
    /// </summary>
    public static int AttackModifier(
        int classAttackModifier,
        int abilityBonus,
        int situational = 0) =>
        checked(classAttackModifier + abilityBonus + situational);

    /// <summary>
    /// The whole derivation for a character: class progression at their level
    /// plus the weapon's governing ability.
    /// </summary>
    public static int AttackModifierFor(
        ClassProgressionTable progression,
        CharacterClass characterClass,
        int level,
        AbilityScores scores,
        bool finesse,
        int situational = 0)
    {
        ArgumentNullException.ThrowIfNull(progression);
        return AttackModifier(
            progression.GetAttackModifier(characterClass, level),
            WeaponAbilityBonus(scores, finesse),
            situational);
    }
}

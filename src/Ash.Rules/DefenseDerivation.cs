namespace Ash.Rules;

/// <summary>
/// How armour, agility and strength combine into a Defense Modifier.
/// </summary>
/// <remarks>
/// <para>
/// Armour is heavy, and strength carries it: a suit's dexterity penalty is
/// offset by the strength bonus, and what remains eats into the defender's
/// dexterity bonus.
/// </para>
/// <para>
/// <b>The penalty reduces the dexterity bonus; it never pushes past zero.</b>
/// Plate on a clumsy strong character is not worse than plate on a statue —
/// it simply means agility stops helping. This diverges from the worked
/// example in the vendored rules, which carries the subtraction below zero
/// (DEX +3, STR +4, plate → −1). See ADR 0006.
/// </para>
/// </remarks>
public static class DefenseDerivation
{
    /// <summary>
    /// The dexterity penalty each armour category imposes before strength.
    /// These live in the rules prose rather than the runtime JSON, so they are
    /// transcribed here with the table they come from.
    /// </summary>
    public static int DexterityPenaltyFor(ArmorType armor) => armor switch
    {
        ArmorType.None => 0,
        ArmorType.Leather => 0,
        ArmorType.Chain => -4,
        ArmorType.Plate => -8,
        _ => throw new ArgumentOutOfRangeException(nameof(armor)),
    };

    /// <summary>
    /// What remains of the armour's penalty once strength has carried what it
    /// can. Never positive: strength offsets weight, it does not add agility.
    /// </summary>
    public static int EffectiveDexterityPenalty(ArmorType armor, int strengthBonus) =>
        Math.Min(0, DexterityPenaltyFor(armor) + strengthBonus);

    /// <summary>
    /// What the defender's dexterity is worth in that armour: the bonus, less
    /// whatever weight strength could not carry, floored at zero.
    /// </summary>
    public static int DexterityContribution(
        ArmorType armor,
        int dexterityBonus,
        int strengthBonus) =>
        Math.Max(
            0,
            dexterityBonus + EffectiveDexterityPenalty(armor, strengthBonus));

    /// <summary>
    /// The full Defense Modifier: the armour's own defence, what agility still
    /// contributes, and whatever the shield and a parrying stance add.
    /// </summary>
    public static int DefenseModifier(
        ArmorType armor,
        int baseDefense,
        int dexterityBonus,
        int strengthBonus,
        int shieldModifier = 0,
        int parryModifier = 0) =>
        checked(
            baseDefense +
            DexterityContribution(armor, dexterityBonus, strengthBonus) +
            shieldModifier +
            parryModifier);
}

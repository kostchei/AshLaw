namespace Ash.Rules;

/// <summary>
/// The three spellcasting traditions carried by
/// <c>spellcasting_rules.armor_restrictions</c>. This is deliberately separate
/// from <see cref="CharacterClass"/>: Druid is a casting tradition in the rules
/// data but is not one of the four progression classes.
/// </summary>
public enum CastingTradition
{
    Wizard,
    Druid,
    Cleric,
}

public enum ShieldKind
{
    None,
    Wooden,
    Metal,
}

/// <summary>Values of <c>armor_restrictions.*.allowed_armor</c>.</summary>
public enum AllowedArmor
{
    None,
    NonMetal,
    All,
}

/// <summary>Values of <c>armor_restrictions.*.allowed_shields</c>.</summary>
public enum AllowedShields
{
    None,
    WoodenOnly,
    All,
}

/// <summary>
/// The outcome of a spellcasting-constraint check. A blocked loadout is a normal
/// game state, not an error, so this is returned rather than thrown; malformed
/// <em>inputs</em> still throw.
/// </summary>
public sealed record SpellcastingCheck(bool IsAllowed, string Reason)
{
    internal static SpellcastingCheck Allowed(string reason) => new(true, reason);

    internal static SpellcastingCheck Blocked(string reason) => new(false, reason);
}

public sealed record ArmorRestriction(
    CastingTradition Tradition,
    AllowedArmor Armor,
    AllowedShields Shields,
    string Description)
{
    /// <summary>
    /// Plate and chain are metal; leather is not. <see cref="ArmorType.None"/> is
    /// permitted by every tradition.
    /// </summary>
    public static bool IsMetal(ArmorType armor) => armor is ArmorType.Plate or ArmorType.Chain;

    public bool Permits(ArmorType armor) => Armor switch
    {
        AllowedArmor.All => true,
        AllowedArmor.NonMetal => !IsMetal(armor),
        AllowedArmor.None => armor == ArmorType.None,
        _ => throw new RulesResolutionException($"Unhandled allowed-armor value '{Armor}'."),
    };

    public bool Permits(ShieldKind shield) => Shields switch
    {
        AllowedShields.All => true,
        AllowedShields.WoodenOnly => shield is ShieldKind.None or ShieldKind.Wooden,
        AllowedShields.None => shield == ShieldKind.None,
        _ => throw new RulesResolutionException($"Unhandled allowed-shield value '{Shields}'."),
    };

    public SpellcastingCheck Check(ArmorType armor, ShieldKind shield)
    {
        if (!Permits(armor))
        {
            return SpellcastingCheck.Blocked(
                $"{Tradition} cannot cast while wearing {armor} armour: {Description}");
        }

        if (!Permits(shield))
        {
            return SpellcastingCheck.Blocked(
                $"{Tradition} cannot cast while carrying a {shield} shield: {Description}");
        }

        return SpellcastingCheck.Allowed(
            $"{Tradition} may cast in {armor} armour with a {shield} shield.");
    }
}

public sealed record VirtueThreshold(int MinimumScore, int RequiredCount);

/// <summary>
/// The cleric's deity-alignment requirement from
/// <c>spellcasting_rules.cleric_deity_alignment</c>: select
/// <see cref="RequiredVirtueCount"/> virtues/vices and hold
/// <see cref="Primary"/> and <see cref="Secondary"/>.
/// </summary>
public sealed record ClericVirtueRequirement(
    int RequiredVirtueCount,
    VirtueThreshold Primary,
    VirtueThreshold Secondary,
    string FailureEffect)
{
    /// <summary>
    /// Checks a cleric's virtue/vice scores against the deity-alignment thresholds.
    /// </summary>
    /// <remarks>
    /// <c>class_progression_tables.md</c> §5 states the requirement as "maintain 3
    /// at ≥15 and 2 at ≥11" over 5 selected virtues. The two tiers are read as
    /// nested rather than disjoint: a score of 15 also satisfies the ≥11 tier, so
    /// the check is "at least 3 scores ≥ 15, and at least 3+2 = 5 scores ≥ 11".
    /// That is equivalent to the prose for the published 5/3/2 values and stays
    /// correct if the data is retuned.
    /// </remarks>
    public SpellcastingCheck Check(IReadOnlyCollection<int> virtueScores)
    {
        ArgumentNullException.ThrowIfNull(virtueScores);
        if (virtueScores.Count != RequiredVirtueCount)
        {
            throw new ArgumentException(
                $"A cleric must have exactly {RequiredVirtueCount} virtue/vice scores; " +
                $"found {virtueScores.Count}.",
                nameof(virtueScores));
        }

        var atPrimary = virtueScores.Count(score => score >= Primary.MinimumScore);
        if (atPrimary < Primary.RequiredCount)
        {
            return SpellcastingCheck.Blocked(
                $"Deity alignment lost: {Primary.RequiredCount} virtues must be at least " +
                $"{Primary.MinimumScore}; only {atPrimary} are. {FailureEffect}");
        }

        var requiredAtSecondary = checked(Primary.RequiredCount + Secondary.RequiredCount);
        var atSecondary = virtueScores.Count(score => score >= Secondary.MinimumScore);
        if (atSecondary < requiredAtSecondary)
        {
            return SpellcastingCheck.Blocked(
                $"Deity alignment lost: {requiredAtSecondary} virtues must be at least " +
                $"{Secondary.MinimumScore}; only {atSecondary} are. {FailureEffect}");
        }

        return SpellcastingCheck.Allowed("Deity alignment maintained.");
    }
}

/// <summary>What a natural-1 spell mishap locks out, and for how long.</summary>
public enum MishapLockoutScope
{
    ThatSpellOnly,
}

public enum MishapLockoutDuration
{
    UntilNextDawn,
}

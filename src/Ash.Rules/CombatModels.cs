namespace Ash.Rules;

public enum AttackCategoryId
{
    OneHandedSlashing,
    OneHandedConcussion,
    TwoHandedWeapons,
    MissileWeapons,
    ToothAndClaw,
    GrapplingUnbalancing,
    SpellBolts,
    SpellBalls,
    EnvironmentalFallCrush,
}

public enum ArmorType
{
    Plate,
    Chain,
    Leather,
    None,
}

public enum CriticalTableId
{
    Crush,
    Slash,
    Puncture,
    Unbalancing,
    Heat,
    Cold,
    Electricity,
    Impact,
}

public enum CriticalTier
{
    A,
    B,
    C,
    D,
    E,
}

/// <summary>
/// How far up the Tooth &amp; Claw result table a physical attack may reach.
/// Source AT-5 caps Tiny, Small, Medium, Large and Huge attacks at chart rows
/// 81-85, 101-105, 116-120, 131-135 and 146-150 respectively.
/// </summary>
public enum AttackSize
{
    Tiny,
    Small,
    Medium,
    Large,
    Huge,
}

public static class AttackSizeRules
{
    /// <summary>The highest net d20 result represented by the size's row.</summary>
    public static int MaximumNetRoll(AttackSize size) => size switch
    {
        AttackSize.Tiny => 17,
        AttackSize.Small => 21,
        AttackSize.Medium => 24,
        AttackSize.Large => 27,
        AttackSize.Huge => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    /// <summary>
    /// The greatest ordinary A-E critical visible at that size cap. The source
    /// also has a Tiny "T" tier, which this runtime does not represent.
    /// </summary>
    public static CriticalTier MaximumCriticalTier(AttackSize size) =>
        size switch
        {
            AttackSize.Tiny => CriticalTier.A,
            AttackSize.Small => CriticalTier.B,
            AttackSize.Medium => CriticalTier.D,
            AttackSize.Large or AttackSize.Huge => CriticalTier.E,
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };
}

public enum TraumaEffectKind
{
    AdditionalHits,
    Bleeding,
    ActivityPenalty,
    Stun,
    Prone,
    Unconscious,
    Death,
    ForcedMovement,
    DropHeldItem,
    BreakItem,
    BreakBone,
    DisableLimb,
    DestroyEye,
    Paralyzed,

    /// <summary>The official 5.5e Restrained condition.</summary>
    Restrained,

    /// <summary>
    /// The official 5.5e Incapacitated condition.
    /// </summary>
    Incapacitated,

    /// <summary>
    /// The zero-hit-point state that starts death saving throws.
    /// </summary>
    Dying,

    /// <summary>The 5.5e suffocation hazard has begun.</summary>
    Suffocating,

    /// <summary>5.5e Exhaustion; Magnitude is the number of levels.</summary>
    Exhaustion,

    /// <summary>
    /// Lasting injury: Disadvantage on Magnitude chosen ability scores and half
    /// Speed until healed.
    /// </summary>
    Injured,

    /// <summary>
    /// Stable at 0 hit points and 0 wounds. Duration is the recovery roll and
    /// Magnitude is the hit points restored on waking.
    /// </summary>
    StableAtZero,

    // 5.5e weapon masteries. These are mechanical markers for the combat-state
    // consumer, not presentation tags; each uses the standard mastery rule.
    /// <summary>Graze: damage equal to the attack's STR or DEX modifier.</summary>
    Graze,

    /// <summary>Vex: Advantage on the next attack against this target.</summary>
    Vex,

    /// <summary>Sap: Disadvantage on the target's next attack.</summary>
    Sap,

    /// <summary>Topple: the mastery save can apply Prone.</summary>
    Topple,

    /// <summary>Cleave: the mastery follow-up attack against a nearby target.</summary>
    Cleave,

    /// <summary>Slow: reduce Speed by 10 feet for the mastery window.</summary>
    Slow,

    /// <summary>Push: move a qualifying target up to 10 feet straight away.</summary>
    Push,
}

public enum TraumaEffectCondition
{
    Always,
    NoArmArmor,
    NoLegArmor,
    NoHelm,
    NoShield,
}

public enum TraumaDurationUnit
{
    None,
    Rounds,
    Hours,
    D4Hours,
    UntilHealed,
    Permanent,
}

public sealed record TraumaEffect(
    TraumaEffectKind Kind,
    int Magnitude = 0,
    int Duration = 0,
    TraumaDurationUnit DurationUnit = TraumaDurationUnit.None,
    TraumaEffectCondition AppliesWhen = TraumaEffectCondition.Always,
    string? Detail = null);

public sealed record AttackRequest(
    int RawD20,
    AttackCategoryId AttackCategory,
    int AttackModifier,
    int DefenseModifier,
    ArmorType Armor,
    CriticalTableId? CriticalTable = null,
    bool IsSpell = false,
    /// <summary>
    /// Opt in to resolving against a table whose curve has never been checked
    /// against a source chart. Defaults to <see langword="false"/> so an
    /// unvalidated table cannot be consumed by accident as if it were sourced.
    /// See <see cref="AttackTable.IsSourceValidated"/>.
    /// </summary>
    bool AllowUnvalidatedTable = false,
    AttackSize? AttackSize = null,
    CriticalTier? MaximumCriticalTier = null);

public sealed record AttackResult
{
    public required bool Hit { get; init; }

    public required int RawD20 { get; init; }

    public required int NetRoll { get; init; }

    public required int Margin { get; init; }

    public required int ConcussionHits { get; init; }

    public CriticalTier? CriticalTier { get; init; }

    public CriticalTableId? CriticalTable { get; init; }

    public int? TraumaIndex { get; init; }

    public string? TraumaText { get; init; }

    public IReadOnlyList<TraumaEffect> TraumaEffects { get; init; } =
        Array.Empty<TraumaEffect>();

    public required bool Mishap { get; init; }

    public IReadOnlyList<string> Messages { get; init; } =
        Array.Empty<string>();

    public int TotalImmediateHits =>
        checked(ConcussionHits + TraumaEffects
            .Where(effect => effect.Kind == TraumaEffectKind.AdditionalHits)
            .Where(effect => effect.AppliesWhen == TraumaEffectCondition.Always)
            .Sum(effect => effect.Magnitude));
}

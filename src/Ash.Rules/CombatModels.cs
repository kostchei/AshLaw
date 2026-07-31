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
    bool AllowUnvalidatedTable = false);

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

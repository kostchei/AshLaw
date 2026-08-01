using Ash.Rules;

namespace Ash.Sim;

/// <summary>
/// What a spell needs named before it can be cast (CMB-043).
/// </summary>
/// <remarks>
/// Three modes, because three are genuinely different questions to ask of the
/// world before committing: nothing (the caster is the target), one creature
/// (which must exist, be alive, and be in range), and one place (which must be
/// on the map and in range). Anything else — a direction, a query over every
/// eligible object — is one of these with a different way of picking, and adding
/// it as a fourth mode before content asks for it would be inventing a rule.
/// </remarks>
public enum SpellTargeting : byte
{
    /// <summary>Resolves in a radius around the caster. Needs no target.</summary>
    SelfCentered = 0,

    /// <summary>Resolves against one named creature at range.</summary>
    SingleActor = 1,

    /// <summary>Resolves in a radius around one named place at range.</summary>
    MapPoint = 2,
}

/// <summary>
/// One spell as content: what it needs, what it costs, how long it takes, and
/// which rules table its attack lands on.
/// </summary>
/// <remarks>
/// A spell owns no resolution of its own. It selects a category from
/// <see cref="AttackCategoryId"/> and a critical table, and the same
/// <see cref="AttackResolver"/> that resolves a sword decides what it did —
/// including the natural-1 mishap, which the rules data already defines for spell
/// categories. The wind-up is in milliseconds because that is what the design
/// speaks in; the clock converts it, and a duration that is not a whole beat is
/// refused rather than rounded.
/// </remarks>
public sealed record SpellDefinition(
    string Id,
    string Name,
    string Description,
    CastingTradition Tradition,
    SpellTargeting Targeting,
    AttackCategoryId Category,
    CriticalTableId CriticalTable,
    int WindUpMilliseconds,
    int RangeTiles,
    int RadiusTiles,
    string ReagentTypeId,
    int ReagentQuantity,
    AttackSize Size,
    CriticalTier MaximumCriticalTier)
{
    /// <summary>The projectile this spell puts in the air when it is released.</summary>
    public string ProjectileId => $"projectile.{Id}";

    public bool NeedsReagents => ReagentQuantity > 0;

    public AttackProfile Attack => new(
        $"attack.{Id}",
        Category,
        CriticalTable,
        Size: Size,
        MaximumCriticalTier: MaximumCriticalTier);

    public ProjectileDefinition Projectile => new(
        ProjectileId,
        Name,
        $"effect.{Id}",
        Category == AttackCategoryId.SpellBalls
            ? ProjectileKind.SpellBall
            : ProjectileKind.SpellBolt,
        Attack,

        // A cast effect crosses the room faster than an arrow but not
        // instantly: it is still a thing that can be walked out from under.
        SpeedTilesPerBeat: 8,
        RangeTiles: RangeTiles,
        BlastRadiusTiles: Targeting == SpellTargeting.SingleActor ? 0 : RadiusTiles);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || !Id.StartsWith("spell.", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Description))
        {
            throw new ArgumentException("A spell needs a spell.* id, a name and a description.", nameof(Id));
        }

        if (Category is not (AttackCategoryId.SpellBolts or AttackCategoryId.SpellBalls))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Category),
                Category,
                "A spell resolves on a spell attack table.");
        }

        if (!Enum.IsDefined(Tradition) || !Enum.IsDefined(Targeting) ||
            !Enum.IsDefined(CriticalTable) || !Enum.IsDefined(Size) ||
            !Enum.IsDefined(MaximumCriticalTier))
        {
            throw new ArgumentException("A spell contains an unknown enum value.", nameof(Id));
        }

        // The wind-up is what makes a cast interruptible at all, so a spell that
        // releases on the beat it is begun is refused rather than silently
        // becoming un-counterable.
        if (WindUpMilliseconds < CombatClock.TickMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(WindUpMilliseconds));
        }

        _ = CombatClock.BeatsIn(WindUpMilliseconds);
        if (RangeTiles < 1 || RadiusTiles < 0 || ReagentQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RangeTiles));
        }

        if (Targeting == SpellTargeting.SingleActor && RadiusTiles != 0)
        {
            throw new ArgumentException(
                "A spell aimed at one creature has no radius.",
                nameof(RadiusTiles));
        }

        if (Targeting != SpellTargeting.SingleActor && RadiusTiles < 1)
        {
            throw new ArgumentException(
                "An area spell needs a radius.",
                nameof(RadiusTiles));
        }

        if (NeedsReagents == string.IsNullOrEmpty(ReagentTypeId))
        {
            throw new ArgumentException(
                "A reagent cost and a reagent type must agree.",
                nameof(ReagentTypeId));
        }

        Projectile.Validate();
    }
}

/// <summary>
/// The authored spell list. Sorcery as the design describes it: destruction
/// aimed at flesh, at stone, and at whatever stands close enough to be drawn
/// from.
/// </summary>
public static class SpellCatalog
{
    /// <summary>The one reagent the destruction list burns.</summary>
    public const string GraveSaltTypeId = "item.grave-salt";

    public const string GraveSaltName = "Grave salt";

    /// <summary>Flesh Destruction: rots living matter at range.</summary>
    public static readonly SpellDefinition WitherFlesh = new(
        "spell.wither-flesh",
        "Wither Flesh",
        "Living matter rots where the bolt lands.",
        CastingTradition.Wizard,
        SpellTargeting.SingleActor,
        AttackCategoryId.SpellBolts,
        CriticalTableId.Heat,
        WindUpMilliseconds: 1000,
        RangeTiles: 8,
        RadiusTiles: 0,
        GraveSaltTypeId,
        ReagentQuantity: 1,
        AttackSize.Medium,
        CriticalTier.D);

    /// <summary>Solid Destruction: shatters what is unliving, and what stands near it.</summary>
    public static readonly SpellDefinition ShatterStone = new(
        "spell.shatter-stone",
        "Shatter Stone",
        "Stone and bone come apart together where the ball bursts.",
        CastingTradition.Wizard,
        SpellTargeting.MapPoint,
        AttackCategoryId.SpellBalls,
        CriticalTableId.Impact,
        WindUpMilliseconds: 1400,
        RangeTiles: 6,
        RadiusTiles: 1,
        GraveSaltTypeId,
        ReagentQuantity: 2,
        AttackSize.Large,
        CriticalTier.E);

    /// <summary>Spellfire: draws life out of everything standing close.</summary>
    public static readonly SpellDefinition SpellfireDraw = new(
        "spell.spellfire-draw",
        "Spellfire Draw",
        "Everything within reach of the caster gives up a little of its life.",
        CastingTradition.Wizard,
        SpellTargeting.SelfCentered,
        AttackCategoryId.SpellBalls,
        CriticalTableId.Heat,
        WindUpMilliseconds: 800,
        RangeTiles: 1,
        RadiusTiles: 2,
        ReagentTypeId: "",
        ReagentQuantity: 0,
        AttackSize.Small,
        CriticalTier.B);

    private static readonly IReadOnlyDictionary<string, SpellDefinition> Index = Build();

    public static IReadOnlyList<SpellDefinition> All =>
        Index.Values.OrderBy(value => value.Id, StringComparer.Ordinal).ToArray();

    /// <summary>Every spell's projectile, for the projectile catalogue to adopt.</summary>
    public static IReadOnlyList<ProjectileDefinition> Projectiles =>
        All.Select(value => value.Projectile).ToArray();

    public static SpellDefinition Get(string spellId) =>
        Index.TryGetValue(spellId, out var spell)
            ? spell
            : throw new InvalidOperationException($"No spell '{spellId}' is authored.");

    public static bool TryGet(string spellId, out SpellDefinition spell) =>
        Index.TryGetValue(spellId, out spell!);

    /// <summary>
    /// Which spell put a given projectile in the air. A mishap is a fact about
    /// the casting, but it is only discovered when the roll happens at impact,
    /// so the impact has to be able to name the spell it came from.
    /// </summary>
    public static bool TrySpellForProjectile(string projectileId, out SpellDefinition spell)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.ProjectileId, projectileId, StringComparison.Ordinal))
            {
                spell = candidate;
                return true;
            }
        }

        spell = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, SpellDefinition> Build()
    {
        var index = new Dictionary<string, SpellDefinition>(StringComparer.Ordinal);
        foreach (var spell in new[] { WitherFlesh, ShatterStone, SpellfireDraw })
        {
            spell.Validate();
            if (!index.TryAdd(spell.Id, spell))
            {
                throw new InvalidOperationException($"Spell '{spell.Id}' is authored twice.");
            }
        }

        return index;
    }
}

using Ash.Rules;
using System.Security.Cryptography;
using System.Text;

namespace Ash.Sim;

/// <summary>
/// The rules identity of one physical attack. Damage curves remain in
/// <see cref="RulesData"/>; a profile selects a curve and supplies only the
/// weapon- or creature-specific adjustments the shared table cannot know.
/// </summary>
public sealed record AttackProfile(
    string Id,
    AttackCategoryId Category,
    CriticalTableId CriticalTable,
    int WeaponAttackModifier = 0,
    AttackSize? Size = null,
    CriticalTier MaximumCriticalTier = CriticalTier.E,
    int MeleeRangeTiles = 1,
    bool IsNatural = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new ArgumentException("An attack profile needs an id.", nameof(Id));
        }

        if (!Enum.IsDefined(Category))
        {
            throw new ArgumentOutOfRangeException(nameof(Category));
        }

        if (!Enum.IsDefined(CriticalTable))
        {
            throw new ArgumentOutOfRangeException(nameof(CriticalTable));
        }

        if (Size is { } size && !Enum.IsDefined(size))
        {
            throw new ArgumentOutOfRangeException(nameof(Size));
        }

        if (!Enum.IsDefined(MaximumCriticalTier))
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCriticalTier));
        }

        if (MeleeRangeTiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MeleeRangeTiles));
        }
    }
}

/// <summary>
/// Immutable combat content keyed by persistent object type id. A save carries
/// the type id and this catalogue supplies its rules meaning; changes therefore
/// require a content-fingerprint change rather than new per-object save fields.
/// </summary>
public sealed class CombatProfileCatalog
{
    public const string RustySwordTypeId = "item.rusty-sword";
    public const string BronzeDaggerTypeId = "item.bronze-dagger";
    public const string GoblinBladeTypeId = "item.notched-blade";
    public const string CaveRatTypeId = "monster.cave-rat";

    public static readonly AttackProfile RustySword = new(
        "attack.rusty-sword",
        AttackCategoryId.OneHandedSlashing,
        CriticalTableId.Slash);

    public static readonly AttackProfile BronzeDagger = new(
        "attack.bronze-dagger",
        AttackCategoryId.OneHandedSlashing,
        CriticalTableId.Puncture,
        WeaponAttackModifier: -3,
        MaximumCriticalTier: CriticalTier.C);

    public static readonly AttackProfile GoblinBlade = new(
        "attack.goblin-blade",
        AttackCategoryId.OneHandedSlashing,
        CriticalTableId.Slash);

    public static readonly AttackProfile CaveRatBite = new(
        "attack.cave-rat-bite",
        AttackCategoryId.ToothAndClaw,
        CriticalTableId.Puncture,
        Size: AttackSize.Small,
        MaximumCriticalTier: CriticalTier.B,
        IsNatural: true);

    public static readonly AttackProfile Unarmed = new(
        "attack.unarmed",
        AttackCategoryId.ToothAndClaw,
        CriticalTableId.Unbalancing,
        Size: AttackSize.Small,
        MaximumCriticalTier: CriticalTier.A,
        IsNatural: true);

    private readonly IReadOnlyDictionary<string, AttackProfile> _weapons;
    private readonly IReadOnlyDictionary<string, AttackProfile> _natural;

    public CombatProfileCatalog(
        IReadOnlyDictionary<string, AttackProfile> weapons,
        IReadOnlyDictionary<string, AttackProfile> natural)
    {
        ArgumentNullException.ThrowIfNull(weapons);
        ArgumentNullException.ThrowIfNull(natural);
        foreach (var profile in weapons.Values.Concat(natural.Values))
        {
            profile.Validate();
        }

        if (weapons.Values.Any(profile => profile.IsNatural))
        {
            throw new ArgumentException(
                "A weapon profile cannot be marked natural.",
                nameof(weapons));
        }

        if (natural.Values.Any(profile => !profile.IsNatural))
        {
            throw new ArgumentException(
                "A natural profile must be marked natural.",
                nameof(natural));
        }

        _weapons = new Dictionary<string, AttackProfile>(
            weapons,
            StringComparer.Ordinal);
        _natural = new Dictionary<string, AttackProfile>(
            natural,
            StringComparer.Ordinal);
    }

    public static CombatProfileCatalog Default { get; } = new(
        new Dictionary<string, AttackProfile>(StringComparer.Ordinal)
        {
            [RustySwordTypeId] = RustySword,
            [BronzeDaggerTypeId] = BronzeDagger,
            [GoblinBladeTypeId] = GoblinBlade,
        },
        new Dictionary<string, AttackProfile>(StringComparer.Ordinal)
        {
            [CaveRatTypeId] = CaveRatBite,
        });

    /// <summary>Canonical identity of every built-in weapon and natural profile.</summary>
    public static string CanonicalFingerprint { get; } = ComputeFingerprint();

    public bool TryWeapon(string typeId, out AttackProfile profile) =>
        _weapons.TryGetValue(typeId, out profile!);

    public AttackProfile NaturalFor(string actorTypeId) =>
        _natural.TryGetValue(actorTypeId, out var profile)
            ? profile
            : Unarmed;

    private static string ComputeFingerprint()
    {
        var profiles = new Dictionary<string, AttackProfile>(StringComparer.Ordinal)
        {
            [RustySwordTypeId] = RustySword,
            [BronzeDaggerTypeId] = BronzeDagger,
            [GoblinBladeTypeId] = GoblinBlade,
            [CaveRatTypeId] = CaveRatBite,
            ["fallback.unarmed"] = Unarmed,
        };
        var canonical = string.Join(
            "\n",
            profiles.OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value =>
                    $"{value.Key}|{value.Value.Id}|{value.Value.Category}|" +
                    $"{value.Value.CriticalTable}|{value.Value.WeaponAttackModifier}|" +
                    $"{value.Value.Size}|{value.Value.MaximumCriticalTier}|" +
                    $"{value.Value.MeleeRangeTiles}|{value.Value.IsNatural}"));
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

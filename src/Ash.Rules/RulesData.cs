using System.Collections.ObjectModel;

namespace Ash.Rules;

public sealed record ArmorTarget(
    int HitThreshold,
    int DamageOrigin,
    decimal Multiplier,
    decimal Quadratic,
    int BaseCriticalA,
    decimal CriticalInterval);

public sealed class AttackTable
{
    private readonly IReadOnlyDictionary<ArmorType, ArmorTarget> _armorTargets;

    internal AttackTable(
        AttackCategoryId id,
        string stableId,
        string csvId,
        string name,
        string defaultCriticalType,
        CriticalTableId? defaultCriticalTable,
        bool canUseShield,
        IDictionary<ArmorType, ArmorTarget> armorTargets)
    {
        Id = id;
        StableId = stableId;
        CsvId = csvId;
        Name = name;
        DefaultCriticalType = defaultCriticalType;
        DefaultCriticalTable = defaultCriticalTable;
        CanUseShield = canUseShield;
        _armorTargets = new ReadOnlyDictionary<ArmorType, ArmorTarget>(
            new Dictionary<ArmorType, ArmorTarget>(armorTargets));
    }

    public AttackCategoryId Id { get; }

    public string StableId { get; }

    public string CsvId { get; }

    public string Name { get; }

    public string DefaultCriticalType { get; }

    public CriticalTableId? DefaultCriticalTable { get; }

    public bool CanUseShield { get; }

    public IReadOnlyDictionary<ArmorType, ArmorTarget> ArmorTargets => _armorTargets;

    public ArmorTarget ForArmor(ArmorType armor) => _armorTargets[armor];
}

public sealed class CriticalOutcome
{
    internal CriticalOutcome(
        CriticalTableId table,
        CriticalTier tier,
        int index,
        string text,
        IEnumerable<TraumaEffect> effects)
    {
        Table = table;
        Tier = tier;
        Index = index;
        Text = text;
        Effects = Array.AsReadOnly(effects.ToArray());
    }

    public CriticalTableId Table { get; }

    public CriticalTier Tier { get; }

    public int Index { get; }

    public string Text { get; }

    public IReadOnlyList<TraumaEffect> Effects { get; }
}

public sealed record SpellcastingRules(
    bool ProvokesInMelee,
    bool InterruptionOnCriticalOrStun,
    int MishapRawD20,
    string MishapEffect,
    string MishapConsequence);

public sealed class RulesData
{
    private readonly IReadOnlyDictionary<AttackCategoryId, AttackTable> _attackTables;
    private readonly IReadOnlyDictionary<(CriticalTableId Table, CriticalTier Tier, int Index), CriticalOutcome>
        _criticalOutcomes;

    internal RulesData(
        IDictionary<AttackCategoryId, AttackTable> attackTables,
        IDictionary<(CriticalTableId Table, CriticalTier Tier, int Index), CriticalOutcome>
            criticalOutcomes,
        SpellcastingRules spellcasting)
    {
        _attackTables = new ReadOnlyDictionary<AttackCategoryId, AttackTable>(
            new Dictionary<AttackCategoryId, AttackTable>(attackTables));
        _criticalOutcomes =
            new ReadOnlyDictionary<(CriticalTableId Table, CriticalTier Tier, int Index), CriticalOutcome>(
                new Dictionary<(CriticalTableId Table, CriticalTier Tier, int Index), CriticalOutcome>(
                    criticalOutcomes));
        Spellcasting = spellcasting;
    }

    public IReadOnlyDictionary<AttackCategoryId, AttackTable> AttackTables => _attackTables;

    public SpellcastingRules Spellcasting { get; }

    public AttackTable GetAttackTable(AttackCategoryId category) =>
        _attackTables.TryGetValue(category, out var table)
            ? table
            : throw new RulesResolutionException($"Unknown attack category '{category}'.");

    public CriticalOutcome GetCriticalOutcome(
        CriticalTableId table,
        CriticalTier tier,
        int index)
    {
        if (index is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Trauma index must be 1 through 10.");
        }

        return _criticalOutcomes.TryGetValue((table, tier, index), out var outcome)
            ? outcome
            : throw new RulesResolutionException(
                $"No critical outcome exists for {table}, Tier {tier}, index {index}.");
    }
}

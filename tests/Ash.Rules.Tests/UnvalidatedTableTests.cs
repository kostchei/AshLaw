namespace Ash.Rules.Tests;

/// <summary>
/// The environmental fall/crush table is deferred, not accepted: it has no
/// supplied source chart, so nothing about its curve has been checked. These
/// tests keep the deferral enforced rather than merely documented.
/// </summary>
public sealed class UnvalidatedTableTests
{
    private static RulesData Rules =>
        RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);

    [Fact]
    public void OnlyTheEnvironmentalTableIsUnvalidated()
    {
        var unvalidated = Rules.AttackTables.Values
            .Where(table => !table.IsSourceValidated)
            .Select(table => table.Id)
            .ToArray();

        Assert.Equal([AttackCategoryId.EnvironmentalFallCrush], unvalidated);
    }

    [Fact]
    public void ResolvingTheDeferredTableRequiresAnExplicitOptIn()
    {
        var request = new AttackRequest(
            15,
            AttackCategoryId.EnvironmentalFallCrush,
            0,
            0,
            ArmorType.Plate,
            CriticalTableId.Crush);

        var exception = Assert.Throws<RulesResolutionException>(
            () => AttackResolver.Resolve(Rules, request));

        Assert.Contains("no supplied source chart", exception.Message, StringComparison.Ordinal);
        Assert.Contains("PROVENANCE.md", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeferredTableStillResolvesWhenExplicitlyRequested()
    {
        var request = new AttackRequest(
            15,
            AttackCategoryId.EnvironmentalFallCrush,
            0,
            0,
            ArmorType.Plate,
            CriticalTableId.Crush,
            AllowUnvalidatedTable: true);

        var result = AttackResolver.Resolve(Rules, request);

        Assert.True(result.Hit);
    }

    [Fact]
    public void ValidatedTablesNeedNoOptIn()
    {
        foreach (var table in Rules.AttackTables.Values.Where(t => t.IsSourceValidated))
        {
            var request = new AttackRequest(
                15,
                table.Id,
                0,
                0,
                ArmorType.Plate,
                CriticalTableId.Crush);

            var result = AttackResolver.Resolve(Rules, request);

            Assert.Equal(15, result.NetRoll);
        }
    }

    /// <summary>
    /// The distinguishing evidence recorded in PROVENANCE: every fitted table
    /// separates the roll at which a hit registers from the roll at which damage
    /// starts accumulating. The environmental table does not, because it was
    /// never fitted.
    /// </summary>
    [Fact]
    public void OnlyTheDeferredTableCollapsesHitThresholdOntoDamageOrigin()
    {
        foreach (var table in Rules.AttackTables.Values)
        {
            var collapsed = table.ArmorTargets.Values
                .All(target => target.HitThreshold == target.DamageOrigin);

            Assert.Equal(!table.IsSourceValidated, collapsed);
        }
    }
}

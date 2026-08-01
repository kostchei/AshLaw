namespace Ash.Rules.Tests;

public sealed class RulesDataLoaderTests
{
    [Fact]
    public void VendoredJsonAndCsvLoadAsACompleteRuleset()
    {
        var rules = RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);

        Assert.Equal(9, rules.AttackTables.Count);
        Assert.All(
            rules.AttackTables.Values,
            table => Assert.Equal(4, table.ArmorTargets.Count));

        foreach (var table in Enum.GetValues<CriticalTableId>())
        {
            foreach (var tier in Enum.GetValues<CriticalTier>())
            {
                for (var index = 1; index <= 10; index++)
                {
                    var outcome = rules.GetCriticalOutcome(table, tier, index);
                    Assert.False(string.IsNullOrWhiteSpace(outcome.Text));
                }
            }
        }
    }

    [Fact]
    public void PackagedTextLoadsTheSameRulesWithoutAFileSystemPath()
    {
        var files = RulesDataLoader.RequiredFileNames.ToDictionary(
            name => name,
            name => File.ReadAllText(Path.Combine(
                RulesTestRepository.DataDirectory,
                name)),
            StringComparer.Ordinal);

        var fromDirectory = RulesDataLoader.LoadFromDirectory(
            RulesTestRepository.DataDirectory);
        var fromText = RulesDataLoader.LoadFromTextFiles(files);
        var request = new AttackRequest(
            17,
            AttackCategoryId.OneHandedSlashing,
            2,
            1,
            ArmorType.Chain,
            CriticalTableId.Slash);

        Assert.Equivalent(
            AttackResolver.Resolve(fromDirectory, request),
            AttackResolver.Resolve(fromText, request),
            strict: true);
        Assert.Equal(
            fromDirectory.ClassProgression.GetAttackModifier(CharacterClass.Fighter, 20),
            fromText.ClassProgression.GetAttackModifier(CharacterClass.Fighter, 20));
    }

    [Fact]
    public void AttackSummaryDisagreementIsRejected()
    {
        using var copy = TemporaryRulesData.Create();
        copy.Replace(
            "attack_tables_summary.csv",
            "AT-1,1-Handed Slashing,Plate,10,8,0.581,0,23,2",
            "AT-1,1-Handed Slashing,Plate,10,8,9.99,0,23,2");

        var exception = Assert.Throws<RulesDataException>(
            () => RulesDataLoader.LoadFromDirectory(copy.DirectoryPath));

        Assert.Contains("disagrees with combat_system_data.json", exception.Message);
    }

    [Fact]
    public void UnknownJsonCategoryIsRejected()
    {
        using var copy = TemporaryRulesData.Create();
        copy.Replace(
            "combat_system_data.json",
            "\"attack_tables\": {",
            "\"attack_tables\": {\n      \"unknown_category\": {},");

        var exception = Assert.Throws<RulesDataException>(
            () => RulesDataLoader.LoadFromDirectory(copy.DirectoryPath));

        Assert.Contains("unknown_category", exception.Message);
        Assert.Contains("unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmptyCriticalOutcomeIsRejected()
    {
        using var copy = TemporaryRulesData.Create();
        copy.Replace(
            "ct_1_crush_critical_table.csv",
            "\"Minor fracture of ribs. Graze.\"",
            "\"\"");

        var exception = Assert.Throws<RulesDataException>(
            () => RulesDataLoader.LoadFromDirectory(copy.DirectoryPath));

        Assert.Contains("trauma text is empty", exception.Message);
    }

    [Fact]
    public void ChangingValidatedDataChangesResolutionWithoutCodeChanges()
    {
        var baselineRules =
            RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);
        var request = new AttackRequest(
            18,
            AttackCategoryId.OneHandedSlashing,
            0,
            0,
            ArmorType.Plate);
        var baseline = AttackResolver.Resolve(baselineRules, request);

        using var copy = TemporaryRulesData.Create();
        copy.Replace(
            "combat_system_data.json",
            "\"multiplier\": 0.581",
            "\"multiplier\": 1.581");
        copy.Replace(
            "attack_tables_summary.csv",
            "AT-1,1-Handed Slashing,Plate,10,8,0.581,0,23,2",
            "AT-1,1-Handed Slashing,Plate,10,8,1.581,0,23,2");
        var modifiedRules = RulesDataLoader.LoadFromDirectory(copy.DirectoryPath);
        var modified = AttackResolver.Resolve(modifiedRules, request);

        Assert.Equal(5, baseline.ConcussionHits);
        Assert.Equal(15, modified.ConcussionHits);
    }
}

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
            "\"Weak grip. No extra damage. +0 hits.\"",
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

    private sealed class TemporaryRulesData : IDisposable
    {
        private static readonly string[] FileNames =
        [
            "combat_system_data.json",
            "attack_tables_summary.csv",
            "ct_1_crush_critical_table.csv",
            "ct_2_slash_critical_table.csv",
            "ct_3_puncture_critical_table.csv",
            "ct_4_unbalancing_critical_table.csv",
        ];

        private TemporaryRulesData(string directoryPath)
        {
            DirectoryPath = directoryPath;
        }

        public string DirectoryPath { get; }

        public static TemporaryRulesData Create()
        {
            var directory = Directory.CreateTempSubdirectory("ash-rules-tests-");
            foreach (var fileName in FileNames)
            {
                File.Copy(
                    Path.Combine(RulesTestRepository.DataDirectory, fileName),
                    Path.Combine(directory.FullName, fileName));
            }

            return new TemporaryRulesData(directory.FullName);
        }

        public void Replace(string fileName, string oldValue, string newValue)
        {
            var path = Path.Combine(DirectoryPath, fileName);
            var original = File.ReadAllText(path);
            var modified = original.Replace(oldValue, newValue, StringComparison.Ordinal);
            Assert.NotEqual(original, modified);
            File.WriteAllText(path, modified);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}

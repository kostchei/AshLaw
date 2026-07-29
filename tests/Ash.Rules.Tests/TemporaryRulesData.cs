namespace Ash.Rules.Tests;

/// <summary>
/// A disposable copy of the vendored rules data, so a test can perturb one value
/// and assert the loader rejects it without touching the repository.
/// </summary>
internal sealed class TemporaryRulesData : IDisposable
{
    private static readonly string[] FileNames =
    [
        "combat_system_data.json",
        "attack_tables_summary.csv",
        "ct_1_crush_critical_table.csv",
        "ct_2_slash_critical_table.csv",
        "ct_3_puncture_critical_table.csv",
        "ct_4_unbalancing_critical_table.csv",
        "class_progression.csv",
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

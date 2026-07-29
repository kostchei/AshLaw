using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ash.Rules.Tests;

/// <summary>
/// Runs only when <c>ASH_REGENERATE_GOLDEN=1</c> is set, so the golden file is
/// never rewritten by an ordinary test run — a snapshot test that silently
/// refreshes its own snapshot proves nothing.
/// </summary>
public sealed class RegenerateGoldenFactAttribute : FactAttribute
{
    public const string Variable = "ASH_REGENERATE_GOLDEN";

    public RegenerateGoldenFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip = $"Set {Variable}=1 to regenerate the golden file.";
        }
    }
}

public sealed class GoldenFileGenerator
{
    private const int SampleCount = 25;
    private const int CaseCount = 10_000;

    /// <summary>Header lines emitted before the first case line.</summary>
    private const int HeaderLines = 2;

    [RegenerateGoldenFact]
    public void RegenerateGoldenFile()
    {
        var document = GoldenResolutionTests.BuildDocument();
        var lines = document.Split('\n');
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document)));

        var builder = new StringBuilder();
        builder.Append("# Golden snapshot of AttackResolver over the deterministic 10,000-case corpus.\n");
        builder.Append("# Regenerate only when the rules data or resolver changes deliberately:\n");
        builder.Append($"#   {RegenerateGoldenFactAttribute.Variable}=1 dotnet test tests/Ash.Rules.Tests\n");
        builder.Append("# See GoldenResolutionTests for the corpus definition.\n");
        builder.Append(CultureInfo.InvariantCulture, $"sha256 {hash}\n");

        for (var sample = 0; sample < SampleCount; sample++)
        {
            var lineIndex = (sample * (CaseCount / SampleCount)) + HeaderLines;
            builder.Append(CultureInfo.InvariantCulture, $"sample {lineIndex} {lines[lineIndex]}\n");
        }

        File.WriteAllText(
            Path.Combine(RulesTestRepository.FixtureDirectory, "attack-resolution.golden"),
            builder.ToString());
    }
}

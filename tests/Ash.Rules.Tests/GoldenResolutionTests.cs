using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ash.Rules.Tests;

/// <summary>
/// Build plan M0 exit proof: a 10,000-case golden test where identical input
/// produces byte-identical output across runs.
/// </summary>
/// <remarks>
/// <para>
/// The corpus is enumerated, not sampled: case <c>i</c> derives every input from
/// <c>i</c> by modular arithmetic with mutually staggered strides. There is no
/// PRNG, so the corpus does not depend on a runtime's random implementation.
/// </para>
/// <para>
/// The golden file stores the SHA-256 of the full canonical document plus evenly
/// spaced sample lines. The hash proves byte-identity; the samples make a
/// regression readable in the diff without committing a megabyte of text. When
/// the hash differs, the test writes the full actual document to the test output
/// directory and names the path.
/// </para>
/// </remarks>
public sealed class GoldenResolutionTests
{
    private const int CaseCount = 10_000;
    private const int SampleCount = 25;
    private const string GoldenFileName = "attack-resolution.golden";

    [Fact]
    public void TenThousandCaseCorpusMatchesTheGoldenFile()
    {
        var document = BuildDocument();
        var actualHash = Sha256(document);
        var golden = ReadGolden();

        if (!string.Equals(actualHash, golden.Hash, StringComparison.Ordinal))
        {
            var dumpPath = Path.Combine(AppContext.BaseDirectory, "attack-resolution.actual.txt");
            File.WriteAllText(dumpPath, document);
            Assert.Fail(
                $"Golden corpus changed.{Environment.NewLine}" +
                $"  expected SHA-256 {golden.Hash}{Environment.NewLine}" +
                $"  actual   SHA-256 {actualHash}{Environment.NewLine}" +
                $"Full actual document written to {dumpPath}.{Environment.NewLine}" +
                "If the rules data changed deliberately, regenerate " +
                $"tests/fixtures/rules/{GoldenFileName}.");
        }

        // The samples are redundant against the hash, but they are what a reviewer
        // actually reads, so they must not silently drift out of date.
        var lines = document.Split('\n');
        foreach (var (index, expected) in golden.Samples)
        {
            Assert.Equal(expected, lines[index]);
        }
    }

    [Fact]
    public void ResolutionIsDeterministicAcrossRepeatedRuns()
    {
        Assert.Equal(BuildDocument(), BuildDocument());
    }

    [Fact]
    public void CorpusCoversEveryCategoryArmorAndRawRoll()
    {
        var rules = RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);
        var categories = new HashSet<AttackCategoryId>();
        var armors = new HashSet<ArmorType>();
        var rolls = new HashSet<int>();
        var tiers = new HashSet<CriticalTier>();
        var hits = 0;
        var mishaps = 0;

        for (var index = 0; index < CaseCount; index++)
        {
            var request = BuildRequest(rules, index);
            categories.Add(request.AttackCategory);
            armors.Add(request.Armor);
            rolls.Add(request.RawD20);

            var result = AttackResolver.Resolve(rules, request);
            if (result.Hit)
            {
                hits++;
            }

            if (result.Mishap)
            {
                mishaps++;
            }

            if (result.CriticalTier is { } tier)
            {
                tiers.Add(tier);
            }
        }

        Assert.Equal(Enum.GetValues<AttackCategoryId>().Length, categories.Count);
        Assert.Equal(Enum.GetValues<ArmorType>().Length, armors.Count);
        Assert.Equal(20, rolls.Count);
        Assert.Equal(Enum.GetValues<CriticalTier>().Length, tiers.Count);
        Assert.True(hits > 0, "The corpus produced no hits.");
        Assert.True(mishaps > 0, "The corpus produced no spell mishaps.");
    }

    internal static string BuildDocument()
    {
        var rules = RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);
        var builder = new StringBuilder();
        builder.Append("ash-attack-resolution-golden v1\n");
        builder.Append(CultureInfo.InvariantCulture, $"cases {CaseCount}\n");

        for (var index = 0; index < CaseCount; index++)
        {
            var request = BuildRequest(rules, index);
            var result = AttackResolver.Resolve(rules, request);
            builder.Append(Render(index, request, result));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Derives case <paramref name="index"/>'s inputs deterministically. The
    /// strides are mutually staggered so the corpus sweeps the space rather than
    /// repeating a short cycle.
    /// </summary>
    private static AttackRequest BuildRequest(RulesData rules, int index)
    {
        var categories = Enum.GetValues<AttackCategoryId>();
        var armors = Enum.GetValues<ArmorType>();
        var criticalTables = Enum.GetValues<CriticalTableId>();

        var category = categories[(index / 20) % categories.Length];
        var table = rules.GetAttackTable(category);

        // A category whose default critical type does not select one physical
        // table must always be given one explicitly, or a critical result has
        // nothing to look up.
        var wantsDefault = index % 5 == 0 && table.DefaultCriticalTable is not null;

        return new AttackRequest(
            RawD20: (index % 20) + 1,
            AttackCategory: category,
            AttackModifier: (index % 23) - 5,
            DefenseModifier: (index % 17) - 3,
            Armor: armors[(index / 7) % armors.Length],
            CriticalTable: wantsDefault ? null : criticalTables[(index / 3) % criticalTables.Length],
            IsSpell: index % 11 == 0,

            // The corpus deliberately sweeps every category, including the
            // deferred environmental table, so it opts in explicitly.
            AllowUnvalidatedTable: !table.IsSourceValidated);
    }

    private static string Render(int index, AttackRequest request, AttackResult result)
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{index}|");
        builder.Append(CultureInfo.InvariantCulture, $"d{request.RawD20}|");
        builder.Append(CultureInfo.InvariantCulture, $"{request.AttackCategory}|");
        builder.Append(CultureInfo.InvariantCulture, $"atk{request.AttackModifier}|");
        builder.Append(CultureInfo.InvariantCulture, $"def{request.DefenseModifier}|");
        builder.Append(CultureInfo.InvariantCulture, $"{request.Armor}|");
        builder.Append(CultureInfo.InvariantCulture, $"{Or(request.CriticalTable)}|");
        builder.Append(CultureInfo.InvariantCulture, $"spell{(request.IsSpell ? 1 : 0)}|");
        builder.Append(CultureInfo.InvariantCulture, $"unval{(request.AllowUnvalidatedTable ? 1 : 0)}");
        builder.Append(" => ");
        builder.Append(CultureInfo.InvariantCulture, $"hit{(result.Hit ? 1 : 0)}|");
        builder.Append(CultureInfo.InvariantCulture, $"net{result.NetRoll}|");
        builder.Append(CultureInfo.InvariantCulture, $"margin{result.Margin}|");
        builder.Append(CultureInfo.InvariantCulture, $"hits{result.ConcussionHits}|");
        builder.Append(CultureInfo.InvariantCulture, $"tier{Or(result.CriticalTier)}|");
        builder.Append(CultureInfo.InvariantCulture, $"ct{Or(result.CriticalTable)}|");
        builder.Append(CultureInfo.InvariantCulture, $"ti{Or(result.TraumaIndex)}|");
        builder.Append(CultureInfo.InvariantCulture, $"mishap{(result.Mishap ? 1 : 0)}|");
        builder.Append(CultureInfo.InvariantCulture, $"total{result.TotalImmediateHits}|");
        builder.Append(CultureInfo.InvariantCulture, $"fx[{RenderEffects(result.TraumaEffects)}]|");
        builder.Append(CultureInfo.InvariantCulture, $"msg{result.Messages.Count}");
        return builder.ToString();
    }

    private static string RenderEffects(IReadOnlyList<TraumaEffect> effects) =>
        string.Join(
            ";",
            effects.Select(effect => string.Create(
                CultureInfo.InvariantCulture,
                $"{effect.Kind}:{effect.Magnitude}:{effect.Duration}:{effect.DurationUnit}:{effect.AppliesWhen}:{effect.Detail ?? "-"}")));

    private static string Or<T>(T? value)
        where T : struct =>
        value.HasValue
            ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "-"
            : "-";

    private static string Sha256(string document) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document)));

    private static (string Hash, IReadOnlyList<(int Index, string Line)> Samples) ReadGolden()
    {
        var path = Path.Combine(RulesTestRepository.FixtureDirectory, GoldenFileName);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Golden file '{path}' is missing.");
        }

        string? hash = null;
        var samples = new List<(int, string)>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("sha256 ", StringComparison.Ordinal))
            {
                hash = line["sha256 ".Length..].Trim();
                continue;
            }

            if (line.StartsWith("sample ", StringComparison.Ordinal))
            {
                var body = line["sample ".Length..];
                var separator = body.IndexOf(' ', StringComparison.Ordinal);
                var index = int.Parse(
                    body[..separator],
                    CultureInfo.InvariantCulture);
                samples.Add((index, body[(separator + 1)..]));
                continue;
            }

            throw new InvalidOperationException($"Unrecognised golden-file line: '{line}'.");
        }

        if (hash is null)
        {
            throw new InvalidOperationException($"Golden file '{path}' has no sha256 line.");
        }

        if (samples.Count != SampleCount)
        {
            throw new InvalidOperationException(
                $"Golden file '{path}' has {samples.Count} samples; expected {SampleCount}.");
        }

        return (hash, samples);
    }
}

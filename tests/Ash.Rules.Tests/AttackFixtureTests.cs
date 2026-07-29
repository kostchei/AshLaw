using System.Text.Json;

namespace Ash.Rules.Tests;

public sealed class AttackFixtureTests
{
    public static TheoryData<string> FixtureFiles =>
        new()
        {
            "greataxe-vs-plate.json",
            "grapple-vs-leather-corrected.json",
            "greataxe-vs-chain-critical-walkthrough.json",
        };

    [Theory]
    [MemberData(nameof(FixtureFiles))]
    public void RulebookFixtureResolvesDeterministically(string fileName)
    {
        var fixture = LoadFixture(fileName);
        var rules = RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);
        var request = new AttackRequest(
            fixture.Request.RawD20,
            Enum.Parse<AttackCategoryId>(fixture.Request.AttackCategory),
            fixture.Request.AttackModifier,
            fixture.Request.DefenseModifier,
            Enum.Parse<ArmorType>(fixture.Request.Armor),
            fixture.Request.CriticalTable is null
                ? null
                : Enum.Parse<CriticalTableId>(fixture.Request.CriticalTable),
            fixture.Request.IsSpell);

        var first = AttackResolver.Resolve(rules, request);
        var second = AttackResolver.Resolve(rules, request);

        Assert.Equal(fixture.Expected.Hit, first.Hit);
        Assert.Equal(fixture.Expected.NetRoll, first.NetRoll);
        Assert.Equal(fixture.Expected.Margin, first.Margin);
        Assert.Equal(fixture.Expected.ConcussionHits, first.ConcussionHits);
        Assert.Equal(
            fixture.Expected.CriticalTier is null
                ? null
                : Enum.Parse<CriticalTier>(fixture.Expected.CriticalTier),
            first.CriticalTier);
        Assert.Equal(fixture.Expected.TraumaIndex, first.TraumaIndex);
        Assert.Equal(fixture.Expected.Mishap, first.Mishap);
        Assert.Equal(fixture.Expected.TraumaText, first.TraumaText);

        Assert.Same(first.TraumaEffects, second.TraumaEffects);
        Assert.Equal(first, second, AttackResultComparer.Instance);
    }

    [Fact]
    public void CorrectedGrappleFixtureHasStructuredConditionalEffects()
    {
        var rules = RulesDataLoader.LoadFromDirectory(RulesTestRepository.DataDirectory);
        var result = AttackResolver.Resolve(
            rules,
            new AttackRequest(
                17,
                AttackCategoryId.GrapplingUnbalancing,
                5,
                0,
                ArmorType.Leather));

        Assert.Contains(
            new TraumaEffect(TraumaEffectKind.AdditionalHits, Magnitude: 5),
            result.TraumaEffects);
        Assert.Contains(
            result.TraumaEffects,
            effect =>
                effect.Kind == TraumaEffectKind.DropHeldItem &&
                effect.Detail == "shield" &&
                effect.AppliesWhen == TraumaEffectCondition.Always);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.AdditionalHits,
                Magnitude: 8,
                AppliesWhen: TraumaEffectCondition.NoShield),
            result.TraumaEffects);
        Assert.Contains(
            new TraumaEffect(
                TraumaEffectKind.Stun,
                Duration: 2,
                DurationUnit: TraumaDurationUnit.Rounds,
                AppliesWhen: TraumaEffectCondition.NoShield),
            result.TraumaEffects);
    }

    private static AttackFixture LoadFixture(string fileName)
    {
        var path = Path.Combine(RulesTestRepository.FixtureDirectory, fileName);
        return JsonSerializer.Deserialize<AttackFixture>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                })
            ?? throw new InvalidOperationException($"Fixture '{path}' deserialized to null.");
    }

    private sealed record AttackFixture(
        string Name,
        string Source,
        FixtureRequest Request,
        FixtureExpected Expected);

    private sealed record FixtureRequest(
        int RawD20,
        string AttackCategory,
        int AttackModifier,
        int DefenseModifier,
        string Armor,
        string? CriticalTable,
        bool IsSpell);

    private sealed record FixtureExpected(
        bool Hit,
        int NetRoll,
        int Margin,
        int ConcussionHits,
        string? CriticalTier,
        int? TraumaIndex,
        bool Mishap,
        string? TraumaText = null);

    private sealed class AttackResultComparer : IEqualityComparer<AttackResult>
    {
        public static AttackResultComparer Instance { get; } = new();

        public bool Equals(AttackResult? left, AttackResult? right) =>
            left is not null &&
            right is not null &&
            left.Hit == right.Hit &&
            left.RawD20 == right.RawD20 &&
            left.NetRoll == right.NetRoll &&
            left.Margin == right.Margin &&
            left.ConcussionHits == right.ConcussionHits &&
            left.CriticalTier == right.CriticalTier &&
            left.CriticalTable == right.CriticalTable &&
            left.TraumaIndex == right.TraumaIndex &&
            left.TraumaText == right.TraumaText &&
            left.TraumaEffects.SequenceEqual(right.TraumaEffects) &&
            left.Mishap == right.Mishap &&
            left.Messages.SequenceEqual(right.Messages);

        public int GetHashCode(AttackResult value) => value.GetHashCode();
    }
}

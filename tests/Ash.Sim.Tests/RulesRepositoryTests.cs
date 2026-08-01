namespace Ash.Sim.Tests;

public sealed class RulesRepositoryTests
{
    [Fact]
    public void RuntimeRuleAndProfileChangesAlterTheContentFingerprint()
    {
        var baseline = RulesRepository.ComputeRuntimeRulesFingerprint(
            new Dictionary<string, string> { ["rules.csv"] = "a,b\n1,2" },
            "creation",
            "vitality");
        var changed = RulesRepository.ComputeRuntimeRulesFingerprint(
            new Dictionary<string, string> { ["rules.csv"] = "a,b\n1,3" },
            "creation",
            "vitality");

        Assert.NotEqual(baseline, changed);
        Assert.Equal(64, CombatProfileCatalog.CanonicalFingerprint.Length);
        Assert.Contains(
            RulesRepository.RuntimeRulesFingerprint,
            PlayableSliceWorld.ContentFingerprint,
            StringComparison.Ordinal);
        Assert.Contains(
            CombatProfileCatalog.CanonicalFingerprint,
            PlayableSliceWorld.ContentFingerprint,
            StringComparison.Ordinal);
    }
}

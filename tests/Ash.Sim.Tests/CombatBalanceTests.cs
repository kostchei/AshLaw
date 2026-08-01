using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class CombatBalanceTests
{
    [Fact]
    public void AuthoredMonsterBodiesBracketResolverDamageWithoutClampingIt()
    {
        var modest = AttackResolver.Resolve(
            RulesRepository.Rules,
            new AttackRequest(
                15,
                AttackCategoryId.OneHandedSlashing,
                1,
                0,
                ArmorType.None,
                CriticalTableId.Slash));
        var severe = AttackResolver.Resolve(
            RulesRepository.Rules,
            new AttackRequest(
                20,
                AttackCategoryId.OneHandedSlashing,
                1,
                0,
                ArmorType.None,
                CriticalTableId.Slash));
        var rat = CombatBalance.DemoMonsterBody(CombatProfileCatalog.CaveRatTypeId);
        var guard = CombatBalance.DemoMonsterBody("monster.goblin-guard");

        Assert.InRange(modest.ConcussionHits, 1, rat.MaximumConcussion - 1);
        Assert.True(severe.ConcussionHits > rat.MaximumConcussion);
        Assert.True(guard.MaximumConcussion > severe.ConcussionHits);
        Assert.Equal(0, rat.MaximumWounds);
        Assert.Equal(0, guard.MaximumWounds);
    }
}

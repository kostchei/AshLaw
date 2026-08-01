namespace Ash.Sim.Tests;

public sealed class MonsterCatalogTests
{
    [Fact]
    public void CatalogueContainsFiveAdaptationsAndFiveGeneratedOriginals()
    {
        Assert.Equal(5, MonsterCatalog.Adapted.Count);
        Assert.Equal(5, MonsterCatalog.SystemGenerated.Count);
        Assert.Equal(10, MonsterCatalog.LevelOne.Count);
        Assert.Equal(
            ["Arachnid Sage", "Amphibious Glare", "Elephantine Reaver",
             "Piscine Mind", "Beastlike Veil"],
            MonsterCatalog.SystemGenerated.Select(value => value.Name));
        Assert.Equal(10, MonsterCatalog.LevelOne
            .Select(value => value.TypeId)
            .Distinct(StringComparer.Ordinal)
            .Count());

        Assert.All(MonsterCatalog.Adapted, profile =>
        {
            Assert.Equal(MonsterOrigin.Adapted, profile.Origin);
            Assert.StartsWith("Shadowdark", profile.Source, StringComparison.Ordinal);
        });
        Assert.All(MonsterCatalog.SystemGenerated, profile =>
        {
            Assert.Equal(MonsterOrigin.SystemGenerated, profile.Origin);
            Assert.NotNull(profile.GeneratorRoll);
            Assert.Equal(0, profile.GeneratorRoll.CombatAdjustment);
            Assert.Equal(11, profile.ArmorClass);
            Assert.Equal(1, profile.AttackBonus);
            Assert.Equal(1, profile.AttackCount);
        });
        Assert.All(MonsterCatalog.LevelOne, profile =>
        {
            profile.Validate();
            Assert.Equal(1, profile.Level);
            Assert.NotEmpty(profile.SpecialDescription);
            Assert.NotEmpty(profile.WeaknessDescription);
            Assert.Equal(profile.Voice, CreatureVoices.All[profile.TypeId]);
        });
    }

    [Fact]
    public void SystemGenerationIsSeededAndReproducible()
    {
        var first = MonsterGenerator.GenerateLevelOne(42, 5);
        var second = MonsterGenerator.GenerateLevelOne(42, 5);
        var different = MonsterGenerator.GenerateLevelOne(43, 5);

        Assert.Equal(first, second);
        Assert.NotEqual(
            first.Select(value => value.TypeId),
            different.Select(value => value.TypeId));
    }

    [Fact]
    public void GeneratorColumnsMatchThePrintedD20Table()
    {
        Assert.Equal(
            [-3, -3, -2, -2, -1, -1, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4],
            Enumerable.Range(1, 20).Select(MonsterGenerator.CombatAdjustmentFor));
        Assert.Equal(
            Enum.GetValues<MonsterQuality>(),
            Enumerable.Range(1, 20).Select(MonsterGenerator.QualityFor));
        Assert.Equal(
            Enum.GetValues<MonsterStrength>(),
            Enumerable.Range(1, 20).Select(MonsterGenerator.StrengthFor));
        Assert.Equal(
            Enum.GetValues<MonsterWeakness>(),
            Enumerable.Range(1, 20).Select(MonsterGenerator.WeaknessFor));
    }

    [Fact]
    public void AOneMutationBossUsesTheFirstColumnAndResolvesFromItsTypeId()
    {
        Assert.Equal(
            Enum.GetValues<MonsterMutation>(),
            Enumerable.Range(1, 12).Select(MonsterMutations.FirstMutation));

        var original = MonsterCatalog.SystemGenerated[0];
        var mutated = MonsterCatalog.WithMutation(original, MonsterMutation.Wings);

        Assert.Equal(MonsterMutation.Wings, mutated.Mutation);
        Assert.Equal(original.TypeId, mutated.BaseTypeId);
        Assert.Equal(3, mutated.TreasureLevel);
        Assert.Equal(MonsterLocomotion.Fly, mutated.Locomotion);
        Assert.Equal(mutated, MonsterCatalog.Get(mutated.TypeId));
    }

    [Fact]
    public void GeneratedWorldActorsUseTheirAuthoredCombatAndMovementStats()
    {
        using var world = PlayableSliceWorld.CreateGenerated(987654321);

        Assert.Equal(2, world.Monsters.Count);
        foreach (var monster in world.Monsters)
        {
            var profile = MonsterCatalog.Get(monster.TypeId);
            var sheet = world.Sheets.For(monster.Id);

            Assert.Equal(profile.MaximumConcussion, monster.MaxHealth);
            Assert.Equal(profile.Armor, sheet.Armor);
            Assert.Equal(profile.DefenseBonus, sheet.DefenseModifier);
            Assert.Equal(profile.Attack, sheet.AttackProfile);
            Assert.Equal(profile.AttackBonus, sheet.AttackModifier);
            Assert.Equal(
                profile.MovementStepsPerRound,
                world.Combat.MovementStepsPerRoundOf(monster.Id));
            Assert.Equal(profile.Voice, CreatureVoices.For(monster.TypeId));
        }
    }

    [Fact]
    public void KillingAMonsterNeverAwardsExperience()
    {
        using var world = PlayableSliceWorld.CreateGenerated(987654321);
        var monster = world.Monsters.First();

        _ = world.Trauma.Apply(
            world.PlayerId,
            monster.Id,
            [new Ash.Rules.TraumaEffect(Ash.Rules.TraumaEffectKind.Death)]);

        Assert.Equal(0, world.Experience);
    }
}

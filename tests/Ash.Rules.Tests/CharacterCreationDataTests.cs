namespace Ash.Rules.Tests;

/// <summary>
/// The creation rules are data. These check the loader refuses a broken file
/// rather than filling gaps in, because a silently patched curve would be worse
/// than a missing one.
/// </summary>
public sealed class CharacterCreationDataTests
{
    private static CharacterCreationData Shipped { get; } =
        CharacterCreationLoader.LoadFromFile(
            Path.Combine(
                RulesTestRepository.Root,
                "data",
                CharacterCreationLoader.FileName));

    [Fact]
    public void TheShippedCurveIsFloorOfScoreMinusTenOverTwo()
    {
        var bonuses = Shipped.AbilityBonuses;

        for (var score = bonuses.MinimumScore; score <= bonuses.MaximumScore; score++)
        {
            Assert.Equal(
                (int)Math.Floor((score - 10) / 2.0),
                bonuses.BonusFor(score));
        }

        // Outside the table is refused, not extrapolated.
        Assert.Throws<RulesResolutionException>(() => bonuses.BonusFor(2));
        Assert.Throws<RulesResolutionException>(() => bonuses.BonusFor(21));
    }

    [Fact]
    public void EveryClassPriorityAndPoolComesFromTheFile()
    {
        var arcana = Shipped.UnearthedArcana;

        Assert.Equal([8, 7, 6, 5, 4, 3], arcana.Pools);
        Assert.Equal(3, arcana.KeepHighest);
        Assert.Equal(
            [
                CharacterClass.Fighter,
                CharacterClass.Cleric,
                CharacterClass.Rogue,
                CharacterClass.Wizard,
            ],
            arcana.ClassPriorities.Keys.Order());
        Assert.Equal(
            Ability.Wisdom,
            arcana.PriorityFor(CharacterClass.Cleric)[0]);
        Assert.Equal(2, Shipped.AncestryOf(Ancestry.Human).TalentRolls);
        Assert.Equal(15, Shipped.Ironman.HighScore);
        Assert.Equal(2, Shipped.Ironman.HighScoresRequired);
        Assert.Equal(6, Shipped.Ironman.LowScore);
        Assert.Equal(1, Shipped.Ironman.LowScoresAllowed);
    }

    [Fact]
    public void AGapInTheBonusTableIsRefused()
    {
        var error = Assert.Throws<RulesDataException>(
            () => CharacterCreationLoader.Parse(
                Document(
                    bonusRows: """[ { "score": 3, "bonus": -4 } ]""",
                    maximumScore: 4)));

        Assert.Contains("score of 4", error.Message);
    }

    [Fact]
    public void AClassPriorityMustNameEachAbilityExactlyOnce()
    {
        var error = Assert.Throws<RulesDataException>(
            () => CharacterCreationLoader.Parse(
                Document(
                    fighterPriority: """
                        ["strength","strength","constitution",
                         "charisma","wisdom","intelligence"]
                        """)));

        Assert.Contains("exactly once", error.Message);
    }

    [Fact]
    public void AnUnknownSchemaVersionIsRefused()
    {
        var error = Assert.Throws<RulesDataException>(
            () => CharacterCreationLoader.Parse(Document(schemaVersion: 99)));

        Assert.Contains("schema", error.Message);
    }

    /// <summary>A minimal valid document, with one part swapped out at a time.</summary>
    private static string Document(
        int schemaVersion = 1,
        string bonusRows = """[ { "score": 3, "bonus": -4 } ]""",
        int maximumScore = 3,
        string fighterPriority = """
            ["strength","dexterity","constitution",
             "charisma","wisdom","intelligence"]
            """) =>
        $$"""
        {
          "schema_version": {{schemaVersion}},
          "ability_bonuses": {
            "minimum_score": 3,
            "maximum_score": {{maximumScore}},
            "rows": {{bonusRows}}
          },
          "ancestries": [
            { "id": "human", "name": "Human", "talent_rolls": 2 }
          ],
          "ironman": {
            "dice_per_score": 3,
            "die_sides": 6,
            "keep_highest": 3,
            "score_order": ["strength","dexterity","constitution",
                            "intelligence","wisdom","charisma"],
            "playable": {
              "high_score": 15,
              "high_scores_required": 2,
              "low_score": 6,
              "low_scores_allowed": 1
            },
            "maximum_attempts": 10
          },
          "unearthed_arcana": {
            "die_sides": 6,
            "keep_highest": 3,
            "pools": [8,7,6,5,4,3],
            "class_priorities": { "fighter": {{fighterPriority}} }
          }
        }
        """;
}

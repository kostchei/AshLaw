using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class WorldPlannerTests
{
    private static readonly CharacterTier Tier =
        new(Level: 1, CharacterClass.Fighter);

    [Fact]
    public void TheSameSeedPlansTheSameWorld()
    {
        var first = WorldPlanner.Plan(20260731, Tier);
        var second = WorldPlanner.Plan(20260731, Tier);

        // Compared as text rather than with Assert.Equal: a record holding an
        // IReadOnlyList compares that list by reference, so record equality
        // would pass here without ever looking at a beat.
        Assert.Equal(Describe(first), Describe(second));
    }

    [Fact]
    public void DifferentSeedsPlanDifferentWorlds()
    {
        var first = WorldPlanner.Plan(20260731, Tier);
        var second = WorldPlanner.Plan(20260732, Tier);

        Assert.NotEqual(Describe(first), Describe(second));
    }

    /// <summary>
    /// The point of deriving each subzone's seed from the world seed and its
    /// index: subzone 7 is the same subzone whether or not the six before it
    /// were ever planned.
    /// </summary>
    [Fact]
    public void ASubzonePlansIdenticallyOnItsOwn()
    {
        var world = WorldPlanner.Plan(4242, Tier);

        for (var index = 0; index < WorldPlanner.SubzoneCount; index++)
        {
            Assert.Equal(
                Describe(world.Subzones[index]),
                Describe(WorldPlanner.PlanSubzone(4242, index, Tier)));
        }
    }

    [Fact]
    public void AWorldIsEighteenSubzonesWithSeventeenRestPointsBetweenThem()
    {
        var world = WorldPlanner.Plan(99, Tier);

        Assert.Equal(18, world.Subzones.Count);
        Assert.Equal(17, world.Transitions.Count);
        Assert.Equal(
            world.Subzones.Select(subzone => subzone.MapId).Distinct().Count(),
            world.Subzones.Count);

        // Map id 0 is the demo map. A generated subzone must not land on it.
        Assert.DoesNotContain(world.Subzones, subzone => subzone.MapId == 0);
    }

    [Fact]
    public void EverySubzoneIsFiveBeatsSpinedByEncounterAndReward()
    {
        var world = WorldPlanner.Plan(7, Tier);

        foreach (var subzone in world.Subzones)
        {
            Assert.Equal(5, subzone.Beats.Count);
            Assert.Equal(BeatKind.Encounter, subzone.Beats[0].Kind);
            Assert.Equal(BeatKind.Reward, subzone.Beats[4].Kind);
            Assert.Equal(
                [
                    BeatKind.Atmospheric,
                    BeatKind.Interactive,
                    BeatKind.Hazard,
                ],
                subzone.Beats
                    .Skip(1)
                    .Take(3)
                    .Select(beat => beat.Kind)
                    .Order());
            Assert.Equal(
                Enumerable.Range(0, 5),
                subzone.Beats.Select(beat => beat.Index));
        }
    }

    /// <summary>
    /// Beats must not overlap and must stay on the map: the builder carves them
    /// straight into terrain, so a plan that overlaps is a plan that silently
    /// loses a room.
    /// </summary>
    [Fact]
    public void BeatsAreDisjointAndInsideTheMap()
    {
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var subzone = WorldPlanner.PlanSubzone(seed, 0, Tier);
            foreach (var beat in subzone.Beats)
            {
                Assert.InRange(beat.Cells.XMin, 0, subzone.Width - 1);
                Assert.InRange(beat.Cells.XMax, 1, subzone.Width);
                Assert.InRange(beat.Cells.YMin, 0, subzone.Depth - 1);
                Assert.InRange(beat.Cells.YMax, 1, subzone.Depth);
            }

            for (var left = 0; left < subzone.Beats.Count; left++)
            {
                for (var right = left + 1; right < subzone.Beats.Count; right++)
                {
                    Assert.False(
                        subzone.Beats[left].Cells
                            .Overlaps(subzone.Beats[right].Cells),
                        $"Seed {seed} beats {left} and {right} overlap.");
                }
            }
        }
    }

    /// <summary>
    /// The traversability guarantee, at the plan level: no corridor asks for a
    /// climb steeper than one Ultima VIII level per cell, which is what the
    /// Avatar clears without rolling a <see cref="VaultCheck"/>.
    /// </summary>
    [Fact]
    public void EveryCorridorRampsWithinOneLevelPerCell()
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            for (var index = 0; index < WorldPlanner.SubzoneCount; index++)
            {
                var subzone = WorldPlanner.PlanSubzone(seed, index, Tier);
                for (var beat = 0; beat < 4; beat++)
                {
                    var ramp = WorldPlanner.CorridorRamp(subzone, beat);
                    var heights = new[] { subzone.Beats[beat].FloorZ }
                        .Concat(ramp)
                        .Append(subzone.Beats[beat + 1].FloorZ)
                        .ToArray();
                    for (var step = 1; step < heights.Length; step++)
                    {
                        Assert.True(
                            Math.Abs(heights[step] - heights[step - 1]) <=
                                PlayableSliceWorld.UnitsPerLevel,
                            $"Seed {seed} subzone {index} corridor {beat} " +
                            $"steps from {heights[step - 1]} to " +
                            $"{heights[step]}, which needs a vault.");
                    }
                }
            }
        }
    }

    [Fact]
    public void FlatSubzonesKeepEveryBeatOnOneLevel()
    {
        var flat = PlansAcross(200)
            .Where(plan => plan.Verticality == Verticality.Flat)
            .ToArray();

        Assert.NotEmpty(flat);
        foreach (var plan in flat)
        {
            Assert.All(plan.Beats, beat => Assert.Equal(0, beat.FloorZ));
        }
    }

    [Fact]
    public void VerticalSubzonesChangeElevation()
    {
        var vertical = PlansAcross(200)
            .Where(plan => plan.Verticality != Verticality.Flat)
            .ToArray();

        Assert.NotEmpty(vertical);
        foreach (var plan in vertical)
        {
            Assert.True(
                plan.Beats.Select(beat => beat.FloorZ).Distinct().Count() > 1,
                $"Subzone {plan.Index} is {plan.Verticality} but flat.");
        }
    }

    /// <summary>
    /// Half flat and half vertical, and the vertical third split three ways.
    /// The bounds are loose because this is a check that the dice are being
    /// read as specified, not a test of the generator's uniformity.
    /// </summary>
    [Fact]
    public void VerticalityFollowsTheSpecifiedOdds()
    {
        var plans = PlansAcross(400).ToArray();
        var counts = plans
            .GroupBy(plan => plan.Verticality)
            .ToDictionary(group => group.Key, group => group.Count());
        var vertical = plans.Length - counts[Verticality.Flat];

        Assert.InRange(counts[Verticality.Flat] / (double)plans.Length, 0.44, 0.56);
        foreach (var kind in new[]
        {
            Verticality.Height,
            Verticality.Depth,
            Verticality.Both,
        })
        {
            Assert.InRange(counts[kind] / (double)vertical, 0.26, 0.40);
        }
    }

    [Fact]
    public void HalfOfRestPointsHaveATrader()
    {
        var transitions = Enumerable.Range(0, 400)
            .SelectMany(seed => Enumerable
                .Range(0, WorldPlanner.SubzoneCount - 1)
                .Select(index =>
                    WorldPlanner.PlanTransition((ulong)seed + 1, index)))
            .ToArray();
        var traded = transitions.Count(transition => transition.HasTrader);

        Assert.InRange(traded / (double)transitions.Length, 0.44, 0.56);

        // Every kind of trader must actually be reachable, or one of the four
        // is dead content that no seed will ever show.
        Assert.Equal(
            4,
            transitions
                .Where(transition => transition.HasTrader)
                .Select(transition => transition.Trader)
                .Distinct()
                .Count());
    }

    [Fact]
    public void ScalingRisesWithLevelAndWithDistanceIntoTheWorld()
    {
        var early = WorldPlanner.PlanSubzone(11, 0, Tier);
        var late = WorldPlanner.PlanSubzone(11, 17, Tier);
        var veteran = WorldPlanner.PlanSubzone(
            11,
            0,
            Tier with { Level = 5 });

        Assert.True(late.MonsterRank > early.MonsterRank);
        Assert.True(veteran.MonsterRank > early.MonsterRank);
        Assert.Equal(early.TreasureRank, early.MonsterRank);

        // The seed decides the shape of a subzone; the tier decides only how
        // hard it hits. Same seed and index must give the same rooms.
        Assert.Equal(
            early.Beats.Select(beat => beat.Cells),
            veteran.Beats.Select(beat => beat.Cells));
    }

    [Fact]
    public void ThemeTagsAreWhatThePresentationLayerReads()
    {
        var forest = WorldPlanner.Plan(3, Tier).Subzones
            .First(plan => plan.Theme == SubzoneTheme.Forest);

        Assert.Equal("theme_forest", forest.ThemeTag);
    }

    [Fact]
    public void PlanningOffTheEndOfTheWorldThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldPlanner.PlanSubzone(1, WorldPlanner.SubzoneCount, Tier));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldPlanner.PlanSubzone(1, -1, Tier));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorldPlanner.PlanTransition(1, WorldPlanner.SubzoneCount - 1));
    }

    [Fact]
    public void ATierNeedsARealLevel()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new CharacterTier(0, CharacterClass.Fighter).Rank);
    }

    /// <summary>
    /// A plan flattened to text, so a comparison actually reads every beat.
    /// </summary>
    private static string Describe(WorldPlan plan) =>
        string.Join(
            "\n",
            new[] { $"{plan.Seed} {plan.Tier.Level} {plan.Tier.Class}" }
                .Concat(plan.Subzones.Select(Describe))
                .Concat(plan.Transitions.Select(transition =>
                    $"{transition.Index} {transition.Seed} " +
                    $"{transition.Trader}")));

    private static string Describe(SubzonePlan plan) =>
        $"{plan.Index} {plan.MapId} {plan.Seed} {plan.Theme} " +
        $"{plan.Verticality} {plan.Width}x{plan.Depth} @{plan.CorridorY} " +
        $"r{plan.MonsterRank}/{plan.TreasureRank} " +
        string.Join(
            ",",
            plan.Beats.Select(beat =>
                $"{beat.Index}:{beat.Kind}:{beat.Cells}:{beat.FloorZ}"));

    private static IEnumerable<SubzonePlan> PlansAcross(int seeds) =>
        Enumerable.Range(0, seeds)
            .SelectMany(seed => Enumerable
                .Range(0, WorldPlanner.SubzoneCount)
                .Select(index =>
                    WorldPlanner.PlanSubzone((ulong)seed + 1, index, Tier)));
}

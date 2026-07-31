using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

/// <summary>
/// Getting over things in the demo world. The crate is the case the rule was
/// written for: waist-high on the Avatar, so some attempts clear it and some
/// do not.
/// </summary>
public sealed class VaultingTests
{
    private const int CrateHeight = 32;
    private const int Tile = PlayableSliceWorld.WorldUnitsPerTile;

    [Fact]
    public void FlatFloorNeverRollsTheDice()
    {
        // "If you try to cross something you get a roll" — so walking across
        // an empty floor must not touch the generator. A stroll that burned
        // dice would also shift every other roll the world makes.
        using var world = PlayableSliceWorld.CreateDemo();
        var before = world.Dice.State;

        Assert.True(world.MovePlayer(0, 1).Succeeded);
        Assert.Equal("Moved.", world.LastMessage);

        Assert.Equal(before, world.Dice.State);
    }

    [Fact]
    public void AWallIsRefusedWithoutARoll()
    {
        // A solid cell is a wall, not a ledge. No roll changes it, so no roll
        // is made.
        using var world = PlayableSliceWorld.CreateDemo();
        PlaceAt(world, new Vec3i(13 * Tile, 25 * Tile, 0));
        var before = world.Dice.State;

        var blocked = world.MovePlayer(0, 1);

        Assert.False(blocked.Succeeded);
        Assert.Equal(before, world.Dice.State);
    }

    [Fact]
    public void SomethingStackedOutOfReachCannotBeLandedOn()
    {
        // The lower crate's top is 32 and worth a try, but the crate stacked
        // on it fills 32 to 64, so there is nowhere to arrive. The attempt is
        // rolled — you did try — and it always fails.
        using var world = PlayableSliceWorld.CreateDemo();
        PlaceBeside(world, "prop.crate-lower", fromDeltaX: -1);

        var blocked = world.MovePlayer(1, 0);

        Assert.False(blocked.Succeeded);
        Assert.Equal(0, world.Player.Location.Position.Z);
    }

    [Fact]
    public void ACrateIsSometimesVaultedAndSometimesNot()
    {
        // The Avatar's dexterity bonus is +1, so a 32-high crate needs a d20
        // of 12 or better. Both outcomes must be reachable, or the check is
        // not doing anything.
        var vaulted = 0;
        var fellShort = 0;
        for (ulong seed = 1; seed <= 40 && (vaulted == 0 || fellShort == 0); seed++)
        {
            using var world = PlayableSliceWorld.CreateDemo(seed);
            PlaceBeside(world, "prop.crate-side", fromDeltaX: 1);

            var attempt = world.MovePlayer(-1, 0);
            if (attempt.Succeeded)
            {
                vaulted++;
                Assert.Contains("You vault it", attempt.Message, StringComparison.Ordinal);
                Assert.Equal(CrateHeight, world.Player.Location.Position.Z);
            }
            else
            {
                fellShort++;
                Assert.Contains("fall short", attempt.Message, StringComparison.Ordinal);
                Assert.Equal(0, world.Player.Location.Position.Z);
            }
        }

        Assert.True(vaulted > 0, "No seed ever cleared the crate.");
        Assert.True(fellShort > 0, "No seed ever failed to clear the crate.");
    }

    [Fact]
    public void AFailedVaultStillCostsTheStepAndLeavesTheWorldValid()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        PlaceBeside(world, "prop.crate-side", fromDeltaX: 1);
        var origin = world.Player.Location.Position;
        var steps = world.Combat.PlayerStepsRemainingInRound;

        var attempt = world.MovePlayer(-1, 0);

        if (attempt.Succeeded)
        {
            Assert.Equal(CrateHeight, world.Player.Location.Position.Z);
            Assert.Equal(
                steps - 1,
                world.Combat.PlayerStepsRemainingInRound);
        }
        else
        {
            // A refused move is a refused move: the budget is only spent on a
            // step that actually commits, and trying does not move you.
            Assert.Equal(origin, world.Player.Location.Position);
            Assert.Equal(steps, world.Combat.PlayerStepsRemainingInRound);
        }

        world.Objects.ValidateInvariants();
        world.Physics.ValidateInvariants();
        world.CurrentMap.ValidateIndex();
    }

    [Fact]
    public void TheStatusLineNamesTheAbilityTheRollAndTheHeight()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        PlaceBeside(world, "prop.crate-side", fromDeltaX: 1);

        var attempt = world.MovePlayer(-1, 0);

        // The Avatar's strength and dexterity are both 12, so the tie goes to
        // dexterity and the bonus is +1.
        Assert.Contains("Dexterity", attempt.Message, StringComparison.Ordinal);
        Assert.Contains("+1", attempt.Message, StringComparison.Ordinal);
        Assert.Contains("high", attempt.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("adv", attempt.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Puts the Avatar in the cell beside a prop, facing it. The Avatar is
    /// placed rather than walked so the round's move is untouched and the dice
    /// have rolled nothing before the attempt under test.
    /// </summary>
    private static void PlaceBeside(
        PlayableSliceWorld world,
        string typeId,
        int fromDeltaX)
    {
        var prop = world.CurrentMap.QueryAll()
            .First(value => value.TypeId == typeId);
        PlaceAt(
            world,
            prop.Location.Position with
            {
                X = prop.Location.Position.X + (fromDeltaX * Tile),
                Z = 0,
            });
    }

    private static void PlaceAt(PlayableSliceWorld world, Vec3i position)
    {
        var placed = world.Transfers.Execute(
            new ObjectTransferRequest(
                world.PlayerId,
                world.Player.Location,
                ObjectLocation.OnMap(PlayableSliceWorld.DemoMapId, position)));
        Assert.True(placed.Succeeded, placed.Message);
    }
}

namespace Ash.Sim.Tests;

public sealed class PlayableSliceTests
{
    [Fact]
    public void DemoStartsWithACharacterBackpackChestsAndMonsters()
    {
        var world = PlayableSliceWorld.CreateDemo();

        Assert.Equal(new GridPosition(2, 5), world.PlayerPosition);
        Assert.Contains("Rusty Sword", world.Backpack.Items);
        Assert.Equal(2, world.Chests.Count);
        Assert.Equal(2, world.Monsters.Count);
        Assert.All(world.Monsters, monster => Assert.True(monster.IsAlive));
    }

    [Fact]
    public void PlayerCanOpenAChestAndLootIntoTheBackpack()
    {
        var world = PlayableSliceWorld.CreateDemo();

        Assert.False(world.ToggleNearestChest().Succeeded);
        MoveToFirstChest(world);

        Assert.True(world.ToggleNearestChest().Succeeded);
        Assert.NotNull(world.ActiveChest);
        Assert.True(world.BackpackOpen);

        var item = world.ActiveChest!.Inventory.Items[0];
        Assert.True(world.TakeFromOpenChest(0).Succeeded);
        Assert.Contains(item, world.Backpack.Items);
        Assert.DoesNotContain(item, world.ActiveChest.Inventory.Items);
    }

    [Fact]
    public void LootTransferIsAtomicWhenBackpackIsFull()
    {
        var world = PlayableSliceWorld.CreateDemo();
        MoveToFirstChest(world);
        world.ToggleNearestChest();

        while (!world.Backpack.IsFull)
        {
            Assert.True(world.Backpack.TryAdd($"Filler {world.Backpack.Items.Count}"));
        }

        var chestCount = world.ActiveChest!.Inventory.Items.Count;
        var result = world.TakeFromOpenChest(0);

        Assert.False(result.Succeeded);
        Assert.Equal(chestCount, world.ActiveChest.Inventory.Items.Count);
    }

    [Fact]
    public void PlayerCanKillAMonsterAndLootItsRemains()
    {
        var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(monster => monster.Id == "cave-rat");

        MoveNextTo(world, rat.Position);
        Assert.True(world.AttackAdjacentMonster().Succeeded);
        Assert.True(rat.IsAlive);
        Assert.True(world.AttackAdjacentMonster().Succeeded);
        Assert.False(rat.IsAlive);

        Assert.True(world.ToggleNearestChest().Succeeded);
        Assert.StartsWith("Remains of", world.ActiveChest!.Name);
        Assert.Contains("Rat Tail", world.ActiveChest.Inventory.Items);
    }

    [Fact]
    public void LivingMonstersAndChestsBlockMovement()
    {
        var world = PlayableSliceWorld.CreateDemo();
        MoveNextTo(world, new GridPosition(5, 4));

        var result = world.MovePlayer(1, 0);

        Assert.False(result.Succeeded);
        Assert.NotEqual(new GridPosition(5, 4), world.PlayerPosition);
    }

    private static void MoveToFirstChest(PlayableSliceWorld world)
    {
        world.MovePlayer(1, 0);
        world.MovePlayer(1, 0);
        world.MovePlayer(0, -1);
    }

    private static void MoveNextTo(PlayableSliceWorld world, GridPosition target)
    {
        while (world.PlayerPosition.X < target.X - 1)
        {
            Assert.True(world.MovePlayer(1, 0).Succeeded);
        }

        while (world.PlayerPosition.X > target.X + 1)
        {
            Assert.True(world.MovePlayer(-1, 0).Succeeded);
        }

        while (world.PlayerPosition.Y < target.Y)
        {
            Assert.True(world.MovePlayer(0, 1).Succeeded);
        }

        while (world.PlayerPosition.Y > target.Y)
        {
            Assert.True(world.MovePlayer(0, -1).Succeeded);
        }
    }
}

namespace Ash.Sim.Tests;

public sealed class PlayableSliceTests
{
    [Fact]
    public void DemoStartsWithACharacterBackpackChestsAndMonsters()
    {
        var world = PlayableSliceWorld.CreateDemo();

        Assert.Equal(new GridPosition(4, 14), world.PlayerPosition);
        Assert.Contains("Rusty Sword", world.Backpack.Items);
        Assert.Equal(4, world.Chests.Count);
        Assert.Equal(4, world.Monsters.Count);
        Assert.True(PlayableSliceWorld.MapWidth > 19);
        Assert.True(PlayableSliceWorld.MapHeight > 13);
        Assert.Contains(
            world.Monsters,
            monster => monster.Id == "many-eyed-tyrant");
        Assert.All(world.Monsters, monster => Assert.True(monster.IsAlive));
        Assert.All(
            world.Chests.Select(chest => chest.Position)
                .Concat(world.Monsters.Select(monster => monster.Position)),
            position =>
            {
                Assert.InRange(position.X, 0, PlayableSliceWorld.MapWidth - 1);
                Assert.InRange(position.Y, 0, PlayableSliceWorld.MapHeight - 1);
            });
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
        var firstChest = world.Chests[0];
        MoveNextTo(world, firstChest.Position);

        var result = world.MovePlayer(1, 0);

        Assert.False(result.Succeeded);
        Assert.NotEqual(firstChest.Position, world.PlayerPosition);
    }

    private static void MoveToFirstChest(PlayableSliceWorld world)
    {
        MoveNextTo(world, world.Chests[0].Position);
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

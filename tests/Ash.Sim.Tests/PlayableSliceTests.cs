namespace Ash.Sim.Tests;

public sealed class PlayableSliceTests
{
    [Fact]
    public void DemoStartsWithACharacterBackpackChestsAndMonsters()
    {
        var world = PlayableSliceWorld.CreateDemo();

        Assert.Equal(new GridPosition(4, 14), world.PlayerPosition);
        Assert.Contains(
            world.BackpackItems,
            item => item.Name == "Rusty Sword");
        Assert.Contains(
            world.BackpackItems,
            item =>
                item.Name == "Rusty Sword" &&
                item.EquipmentSlots == EquipmentSlotMask.MainHand);
        Assert.Equal(4, world.Chests.Count);
        Assert.Equal(4, world.Monsters.Count);
        Assert.True(PlayableSliceWorld.MapWidth > 19);
        Assert.True(PlayableSliceWorld.MapHeight > 13);
        Assert.Contains(
            world.Monsters,
            monster => monster.TypeId == "monster.many-eyed-tyrant");
        Assert.All(world.Monsters, monster => Assert.True(monster.IsAlive));
        Assert.All(
            world.Chests.Select(chest => world.GetGridPosition(chest.Id))
                .Concat(world.Monsters.Select(monster =>
                    world.GetGridPosition(monster.Id))),
            position =>
            {
                Assert.InRange(position.X, 0, PlayableSliceWorld.MapWidth - 1);
                Assert.InRange(position.Y, 0, PlayableSliceWorld.MapHeight - 1);
            });
        world.Objects.ValidateInvariants();
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

        var chest = world.ActiveChest!.Value;
        var item = world.ContentsOf(chest.Id)[0];
        Assert.True(world.TakeFromOpenChest(0).Succeeded);
        Assert.Contains(
            world.BackpackItems,
            candidate => candidate.Id == item.Id);
        Assert.DoesNotContain(
            world.ContentsOf(chest.Id),
            candidate => candidate.Id == item.Id);
    }

    [Fact]
    public void LootTransferIsAtomicWhenBackpackIsFull()
    {
        var world = PlayableSliceWorld.CreateDemo();
        MoveToFirstChest(world);
        world.ToggleNearestChest();

        while (world.BackpackItems.Count < world.BackpackCapacity)
        {
            var index = world.BackpackItems.Count;
            world.Objects.Create(new ObjectSpawn
            {
                TypeId = $"item.filler-{index}",
                Name = $"Filler {index}",
                ShapeId = "loot.generic",
                Location = ObjectLocation.InContainer(world.PlayerId),
                Flags = ObjectFlags.Item | ObjectFlags.Movable,
                Footprint = new ObjectFootprint(32, 32),
                Height = 8,
            });
        }

        var chest = world.ActiveChest!.Value;
        var chestCount = world.ContentsOf(chest.Id).Count;
        var result = world.TakeFromOpenChest(0);

        Assert.False(result.Succeeded);
        Assert.Equal(chestCount, world.ContentsOf(chest.Id).Count);
    }

    [Fact]
    public void OneSwordMovesThroughBackpackEquipmentWorldAndChest()
    {
        var world = PlayableSliceWorld.CreateDemo();
        var sword = world.BackpackItems.Single(
            item => item.Name == "Rusty Sword");
        var swordIndex = world.BackpackItems
            .Select((item, index) => (item, index))
            .Single(pair => pair.item.Id == sword.Id)
            .index;

        Assert.True(world.EquipFromBackpack(swordIndex).Succeeded);
        Assert.Equal(
            LocationKind.Equipped,
            world.Objects.Get(sword.Id).Location.Kind);
        Assert.Equal(
            sword.Id,
            world.EquippedIn(EquipmentSlot.MainHand)!.Value.Id);

        Assert.True(
            world.UnequipToBackpack(EquipmentSlot.MainHand).Succeeded);
        swordIndex = world.BackpackItems
            .Select((item, index) => (item, index))
            .Single(pair => pair.item.Id == sword.Id)
            .index;
        Assert.True(world.DropFromBackpack(swordIndex).Succeeded);
        Assert.Equal(world.Player.Location, world.Objects.Get(sword.Id).Location);
        Assert.Contains(world.GroundItems, item => item.Id == sword.Id);

        Assert.True(world.PickUpAtPlayerFeet().Succeeded);
        Assert.Contains(world.BackpackItems, item => item.Id == sword.Id);

        MoveToFirstChest(world);
        Assert.True(world.ToggleNearestChest().Succeeded);
        swordIndex = world.BackpackItems
            .Select((item, index) => (item, index))
            .Single(pair => pair.item.Id == sword.Id)
            .index;
        Assert.True(world.PutInOpenChest(swordIndex).Succeeded);
        var chest = world.ActiveChest!.Value;
        var chestIndex = world.ContentsOf(chest.Id)
            .Select((item, index) => (item, index))
            .Single(pair => pair.item.Id == sword.Id)
            .index;
        Assert.True(world.TakeFromOpenChest(chestIndex).Succeeded);

        var final = world.Objects.Get(sword.Id);
        Assert.Equal(sword.Id, final.Id);
        Assert.Equal(
            ObjectLocation.InContainer(world.PlayerId),
            final.Location);
        world.Objects.ValidateInvariants();
    }

    [Fact]
    public void FailedUnequipKeepsItemEquippedWhenBackpackIsFull()
    {
        var world = PlayableSliceWorld.CreateDemo();
        Assert.True(world.ToggleMainHand().Succeeded);
        var sword = world.EquippedIn(EquipmentSlot.MainHand)!.Value;

        while (world.BackpackItems.Count < world.BackpackCapacity)
        {
            var index = world.BackpackItems.Count;
            world.Objects.Create(new ObjectSpawn
            {
                TypeId = $"item.filler-{index}",
                Name = $"Filler {index}",
                ShapeId = "loot.generic",
                Location = ObjectLocation.InContainer(world.PlayerId),
                Flags = ObjectFlags.Item | ObjectFlags.Movable,
                Footprint = new ObjectFootprint(32, 32),
                Height = 8,
            });
        }

        var result = world.UnequipToBackpack(EquipmentSlot.MainHand);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocationKind.Equipped,
            world.Objects.Get(sword.Id).Location.Kind);
        Assert.Equal(
            sword.Id,
            world.EquippedIn(EquipmentSlot.MainHand)!.Value.Id);
    }

    [Fact]
    public void PlayerCanKillAMonsterAndLootItsRemains()
    {
        var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(
            monster => monster.TypeId == "monster.cave-rat");

        MoveNextTo(world, world.GetGridPosition(rat.Id));
        Assert.True(world.AttackAdjacentMonster().Succeeded);
        Assert.True(world.Objects.Get(rat.Id).IsAlive);
        Assert.True(world.AttackAdjacentMonster().Succeeded);
        var corpse = world.Objects.Get(rat.Id);
        Assert.False(corpse.IsAlive);
        Assert.True(corpse.HasFlag(ObjectFlags.Corpse));

        Assert.True(world.ToggleNearestChest().Succeeded);
        Assert.Equal(rat.Id, world.ActiveChest!.Value.Id);
        Assert.StartsWith("Remains of", world.ActiveChest.Value.Name);
        Assert.Contains(
            world.ContentsOf(rat.Id),
            item => item.Name == "Rat Tail");
    }

    [Fact]
    public void LivingMonstersAndChestsBlockMovement()
    {
        var world = PlayableSliceWorld.CreateDemo();
        var firstChest = world.Chests[0];
        MoveNextTo(world, world.GetGridPosition(firstChest.Id));

        var result = world.MovePlayer(1, 0);

        Assert.False(result.Succeeded);
        Assert.NotEqual(
            world.GetGridPosition(firstChest.Id),
            world.PlayerPosition);
    }

    private static void MoveToFirstChest(PlayableSliceWorld world)
    {
        MoveNextTo(
            world,
            world.GetGridPosition(world.Chests[0].Id));
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

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
        Assert.Equal(20, world.Map.IndexedObjectCount);
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
        world.Map.ValidateIndex();
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
        Assert.Equal(21, world.Map.IndexedObjectCount);

        Assert.True(world.PickUpAtPlayerFeet().Succeeded);
        Assert.Contains(world.BackpackItems, item => item.Id == sword.Id);
        Assert.Equal(20, world.Map.IndexedObjectCount);

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

    [Fact]
    public void SolidTerrainBlocksTheAvatarAndNamesTheCell()
    {
        var world = PlayableSliceWorld.CreateDemo();

        while (world.PlayerPosition.Y > 3)
        {
            Assert.True(world.MovePlayer(0, -1).Succeeded);
        }

        Assert.True(world.MovePlayer(-1, 0).Succeeded);
        Assert.Equal(new GridPosition(3, 3), world.PlayerPosition);

        var result = world.MovePlayer(-1, 0);

        Assert.False(result.Succeeded);
        Assert.Contains("(2, 3)", result.Message);
        Assert.Equal(new GridPosition(3, 3), world.PlayerPosition);
        world.Map.ValidateIndex();
    }

    [Fact]
    public void NoSuccessfulMoveLeavesOverlappingSolidVolumes()
    {
        var world = PlayableSliceWorld.CreateDemo();

        for (var step = 0; step < 40; step++)
        {
            world.MovePlayer(step % 3 == 0 ? 0 : 1, step % 3 == 0 ? 1 : 0);
            var solids = world.Map.QueryAll(ObjectFlags.Solid);
            foreach (var first in solids)
            {
                foreach (var second in solids)
                {
                    Assert.True(
                        first.Id == second.Id ||
                        !WorldMap.VolumeFor(first)
                            .Overlaps(WorldMap.VolumeFor(second)),
                        $"{first.Name} overlaps {second.Name}.");
                }
            }
        }

        world.Objects.ValidateInvariants();
        world.Map.ValidateIndex();
    }

    [Fact]
    public void TheAvatarClimbsTheStairsOntoTheRaisedPlatform()
    {
        var world = PlayableSliceWorld.CreateDemo();
        WalkTo(world, new GridPosition(25, 5));
        Assert.Equal(0, world.Player.Location.Position.Z);

        for (var step = 1; step <= 4; step++)
        {
            var climb = world.MovePlayer(1, 0);
            Assert.True(climb.Succeeded, climb.Message);
            Assert.Equal(
                step * PlayableSliceWorld.UnitsPerLevel,
                world.Player.Location.Position.Z);
        }

        Assert.True(world.MovePlayer(1, 0).Succeeded);
        Assert.Equal(
            PlayableSliceWorld.PlatformZ,
            world.Player.Location.Position.Z);
        Assert.Equal(SupportKind.Terrain, world.Player.Support.Kind);
        world.Physics.ValidateInvariants();
    }

    [Fact]
    public void LootOnFurnitureRestsAtTheTopFaceOfWhatHoldsIt()
    {
        var world = PlayableSliceWorld.CreateDemo();

        var candlestick = world.GroundItems.Single(
            item => item.Name == "Brass Candlestick");
        var table = world.Map.QueryAll().Single(
            value => value.TypeId == "prop.oak-table");

        Assert.Equal(MotionState.Resting, candlestick.Motion);
        Assert.Equal(SupportKind.Object, candlestick.Support.Kind);
        Assert.Equal(table.Id, candlestick.Support.ObjectId);
        Assert.Equal(
            table.Location.Position.Z + table.Height,
            candlestick.Location.Position.Z);
        Assert.Equal(
            [candlestick.Id],
            world.Map.SupportedObjects(table.Id));
        world.Physics.ValidateInvariants();
    }

    [Fact]
    public void RemovingTheTrestleDropsEverythingItHeld()
    {
        var world = PlayableSliceWorld.CreateDemo();
        var urn = world.GroundItems.Single(item => item.Name == "Clay Urn");
        Assert.True(world.Objects.Get(urn.Id).Location.Position.Z > 0);

        Assert.True(world.RemoveTrestleSupport().Succeeded);
        for (var tick = 0; tick < 100; tick++)
        {
            world.AdvancePhysics();
            if (world.Objects.Get(urn.Id).Motion == MotionState.Resting)
            {
                break;
            }
        }

        var landed = world.Objects.Get(urn.Id);
        Assert.Equal(MotionState.Resting, landed.Motion);
        Assert.Equal(0, landed.Location.Position.Z);
        Assert.Equal(SupportKind.Terrain, landed.Support.Kind);
        world.Physics.ValidateInvariants();
        world.Map.ValidateIndex();
    }

    [Fact]
    public void TheBridgeCarriesTheAvatarAndThePitCatchesTheFall()
    {
        var world = PlayableSliceWorld.CreateDemo();
        WalkTo(
            world,
            new GridPosition(
                PlayableSliceWorld.PitXMin - 1,
                PlayableSliceWorld.BridgeY));

        Assert.True(world.MovePlayer(1, 0).Succeeded);
        Assert.Equal(4, world.Player.Location.Position.Z);
        Assert.Equal(SupportKind.Object, world.Player.Support.Kind);

        Assert.True(world.MovePlayer(0, 1).Succeeded);
        Assert.Equal(MotionState.Falling, world.Player.Motion);

        for (var tick = 0; tick < 100 &&
            world.Player.Motion == MotionState.Falling; tick++)
        {
            world.AdvancePhysics();
        }

        Assert.Equal(
            PlayableSliceWorld.PitFloorZ,
            world.Player.Location.Position.Z);
        Assert.Equal(SupportKind.Terrain, world.Player.Support.Kind);
        world.Physics.ValidateInvariants();
    }

    [Fact]
    public void TheDemoWorldSavesAndLoadsWithoutSettlingAgain()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ash-slice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "demo.ashw");
            var world = PlayableSliceWorld.CreateDemo();
            WalkTo(world, new GridPosition(25, 5));
            for (var step = 0; step < 3; step++)
            {
                Assert.True(world.MovePlayer(1, 0).Succeeded);
            }

            // Step off the raised stairs so an object is mid-fall when saved.
            var urn = world.GroundItems.Single(item => item.Name == "Clay Urn");
            Assert.True(world.RemoveTrestleSupport().Succeeded);
            world.AdvancePhysics();
            Assert.Equal(
                MotionState.Falling,
                world.Objects.Get(urn.Id).Motion);
            var savedUrn = world.Objects.Get(urn.Id);
            var savedPlayer = world.Player;
            var savedTick = world.Physics.Tick;
            var savedTerrain = world.Map.GetTerrain(
                PlayableSliceWorld.PlatformXMin,
                PlayableSliceWorld.TerraceYMin);

            world.Save(path);
            var loaded = PlayableSliceWorld.Load(path);

            Assert.Equal(savedTick, loaded.Physics.Tick);
            Assert.Equal(savedPlayer, loaded.Player);
            Assert.Equal(savedUrn, loaded.Objects.Get(urn.Id));
            Assert.Equal(
                savedTerrain,
                loaded.Map.GetTerrain(
                    PlayableSliceWorld.PlatformXMin,
                    PlayableSliceWorld.TerraceYMin));
            Assert.Equal(
                world.Map.IndexedObjectCount,
                loaded.Map.IndexedObjectCount);

            // Loading resumes rather than settles: the urn is still falling,
            // and one tick on each world lands it in the same place.
            Assert.Equal(
                MotionState.Falling,
                loaded.Objects.Get(urn.Id).Motion);
            world.AdvancePhysics();
            loaded.AdvancePhysics();
            Assert.Equal(
                world.Objects.Get(urn.Id),
                loaded.Objects.Get(urn.Id));
            loaded.Physics.ValidateInvariants();
            loaded.Map.ValidateIndex();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // The demo's props and monsters sit on the rows the Avatar starts on, so
    // the walk clears the column first and then runs along the empty row.
    private static void WalkTo(PlayableSliceWorld world, GridPosition target)
    {
        while (world.PlayerPosition.Y != target.Y)
        {
            var step = world.PlayerPosition.Y < target.Y ? 1 : -1;
            var move = world.MovePlayer(0, step);
            Assert.True(move.Succeeded, move.Message);
        }

        while (world.PlayerPosition.X != target.X)
        {
            var step = world.PlayerPosition.X < target.X ? 1 : -1;
            var move = world.MovePlayer(step, 0);
            Assert.True(move.Succeeded, move.Message);
        }
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

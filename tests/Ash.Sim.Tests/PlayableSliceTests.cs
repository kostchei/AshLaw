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
                item.EquipmentSlots == EquipmentSlotMask.RightHand);
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
    public void AFullPackSendsFurtherLootToTheFloor()
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

        Assert.Equal(world.BackpackCapacity, world.BackpackSlotsUsed);
        var chest = world.ActiveChest!.Value;
        var loot = world.ContentsOf(chest.Id)[0];

        var result = world.TakeFromOpenChest(0);

        // Gear slots are a hard limit, but a full pack does not stop you
        // taking things: the overflow lands at your feet.
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("falls at your feet", result.Message);
        Assert.DoesNotContain(
            world.BackpackItems,
            item => item.Id == loot.Id);
        Assert.DoesNotContain(
            world.ContentsOf(chest.Id),
            item => item.Id == loot.Id);
        Assert.Equal(
            world.PlayerPosition,
            world.GetGridPosition(loot.Id));
        Assert.Equal(world.BackpackCapacity, world.BackpackSlotsUsed);
        world.Objects.ValidateInvariants();
        world.Physics.ValidateInvariants();
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
            world.EquippedIn(EquipmentSlot.RightHand)!.Value.Id);

        Assert.True(
            world.UnequipToBackpack(EquipmentSlot.RightHand).Succeeded);
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
        Assert.True(world.ToggleRightHand().Succeeded);
        var sword = world.EquippedIn(EquipmentSlot.RightHand)!.Value;

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

        var result = world.UnequipToBackpack(EquipmentSlot.RightHand);

        Assert.False(result.Succeeded);
        Assert.Equal(
            LocationKind.Equipped,
            world.Objects.Get(sword.Id).Location.Kind);
        Assert.Equal(
            sword.Id,
            world.EquippedIn(EquipmentSlot.RightHand)!.Value.Id);
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

        // The second blow costs a second round.
        CombatRound.WaitForPlayerSwing(world);
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
    public void ALivingMonsterIsGoneRoundRatherThanVaulted()
    {
        // A body is not scenery. Whatever the dice say, you do not hurdle a
        // goblin, so the move is refused without a roll.
        var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(
            monster => monster.TypeId == "monster.cave-rat");
        MoveNextTo(world, world.GetGridPosition(rat.Id));
        var before = world.Dice.State;

        var result = CombatRound.Step(world, 1, 0);

        Assert.False(result.Succeeded);
        Assert.NotEqual(world.GetGridPosition(rat.Id), world.PlayerPosition);
        Assert.Equal(before, world.Dice.State);
    }

    [Fact]
    public void AChestIsWaistHighSoGettingOverItIsRolledFor()
    {
        // A chest stands 40 high, inside what a vault can reach, so it stopped
        // being a wall the moment vaulting became a check. Either outcome is
        // legitimate; what matters is that it was decided by a roll.
        var world = PlayableSliceWorld.CreateDemo();
        var firstChest = world.Chests[0];
        var chestCell = world.GetGridPosition(firstChest.Id);
        MoveNextTo(world, chestCell);
        var before = world.Dice.State;

        var result = CombatRound.Step(world, 1, 0);

        Assert.NotEqual(before, world.Dice.State);
        if (result.Succeeded)
        {
            Assert.Equal(chestCell, world.PlayerPosition);
            Assert.Equal(
                firstChest.Height,
                world.Player.Location.Position.Z);
        }
        else
        {
            Assert.NotEqual(chestCell, world.PlayerPosition);
        }

        world.Objects.ValidateInvariants();
        world.Physics.ValidateInvariants();
    }

    [Fact]
    public void SolidTerrainBlocksTheAvatarAndNamesTheCell()
    {
        var world = PlayableSliceWorld.CreateDemo();

        while (world.PlayerPosition.Y > 3)
        {
            Assert.True(CombatRound.Step(world, 0, -1).Succeeded);
        }

        Assert.True(CombatRound.Step(world, -1, 0).Succeeded);
        Assert.Equal(new GridPosition(3, 3), world.PlayerPosition);

        var result = CombatRound.Step(world, -1, 0);

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
            CombatRound.Step(world, step % 3 == 0 ? 0 : 1, step % 3 == 0 ? 1 : 0);
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
            var climb = CombatRound.Step(world, 1, 0);
            Assert.True(climb.Succeeded, climb.Message);
            Assert.Equal(
                step * PlayableSliceWorld.UnitsPerLevel,
                world.Player.Location.Position.Z);
        }

        Assert.True(CombatRound.Step(world, 1, 0).Succeeded);
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

        Assert.True(CombatRound.Step(world, 1, 0).Succeeded);
        Assert.Equal(4, world.Player.Location.Position.Z);
        Assert.Equal(SupportKind.Object, world.Player.Support.Kind);

        Assert.True(CombatRound.Step(world, 0, 1).Succeeded);
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
    public void DraggingMovesLootFromTheWorldToThePackAndBackToTheFloor()
    {
        var world = PlayableSliceWorld.CreateDemo();
        var candlestick = world.GroundItems.Single(
            item => item.Name == "Brass Candlestick");
        WalkTo(world, world.GetGridPosition(candlestick.Id).Offset(0, 1));

        Assert.True(world.BeginDrag(candlestick.Id).Succeeded);
        Assert.Equal(candlestick.Id, world.HeldObject!.Value.Id);
        Assert.DoesNotContain(
            world.GroundItems,
            item => item.Id == candlestick.Id);

        Assert.True(world.DropDragInBackpack().Succeeded);
        Assert.Null(world.HeldObject);
        Assert.Contains(
            world.BackpackItems,
            item => item.Id == candlestick.Id);

        // Back out of the pack onto the floor beside the Avatar, which lands it
        // on whatever surface is under that cell.
        Assert.True(world.BeginDrag(candlestick.Id).Succeeded);
        var target = world.PlayerPosition.Offset(0, 1);
        Assert.True(world.DropDragOnMap(target).Succeeded);
        var dropped = world.Objects.Get(candlestick.Id);
        Assert.Equal(target, world.GetGridPosition(candlestick.Id));
        Assert.Equal(MotionState.Resting, dropped.Motion);
        Assert.False(dropped.Support.IsNone);
        world.Physics.ValidateInvariants();
        world.Map.ValidateIndex();
    }

    [Fact]
    public void TwoFindsOfGoldBecomeOnePurseInTheBackpack()
    {
        var world = PlayableSliceWorld.CreateDemo();
        var chests = world.Chests
            .Where(chest => world.ContentsOf(chest.Id)
                .Any(item => item.Name == "Gold Coins"))
            .ToArray();
        Assert.Equal(2, chests.Length);

        var carried = 0;
        foreach (var chest in chests)
        {
            // Stand west of each chest: walking onto the one just looted would
            // be blocked by the chest itself.
            WalkTo(world, world.GetGridPosition(chest.Id).Offset(-1, 0));
            Assert.True(world.ToggleNearestChest().Succeeded);
            var contents = world.ContentsOf(chest.Id);
            var goldIndex = contents
                .Select((item, index) => (item, index))
                .Single(pair => pair.item.Name == "Gold Coins")
                .index;
            carried += contents[goldIndex].Quantity;
            Assert.True(world.TakeFromOpenChest(goldIndex).Succeeded);
        }

        var purses = world.BackpackItems
            .Where(item => item.Name == "Gold Coins")
            .ToArray();

        // Two chests, one purse: 12 and 18 arrive as a single stack of 30.
        Assert.Single(purses);
        Assert.Equal(carried, purses[0].Quantity);
        Assert.Equal(30, purses[0].Quantity);
        world.Objects.ValidateInvariants();
    }

    [Fact]
    public void DroppingGoldOntoGoldMergesInsteadOfStackingUpRows()
    {
        var world = PlayableSliceWorld.CreateDemo();
        var chest = world.Chests.First(candidate =>
            world.ContentsOf(candidate.Id)
                .Any(item => item.Name == "Gold Coins"));
        MoveNextTo(world, world.GetGridPosition(chest.Id));
        Assert.True(world.ToggleNearestChest().Succeeded);
        var gold = world.ContentsOf(chest.Id)
            .Single(item => item.Name == "Gold Coins");

        // Split a few coins into the pack, then drag the rest onto them.
        var split = world.Stacks.Split(
            gold.Id,
            4,
            ObjectLocation.InContainer(world.PlayerId));
        Assert.True(split.Succeeded, split.Message);
        Assert.Equal(8, world.Objects.Get(gold.Id).Quantity);

        Assert.True(world.BeginDrag(gold.Id).Succeeded);
        var dropped = world.DropDragInBackpack();

        Assert.True(dropped.Succeeded, dropped.Message);
        var purse = Assert.Single(
            world.BackpackItems.Where(item => item.Name == "Gold Coins"));
        Assert.Equal(12, purse.Quantity);
        Assert.False(world.Objects.TryGet(gold.Id, out _));
        Assert.Null(world.HeldObject);
        world.Objects.ValidateInvariants();
    }

    [Fact]
    public void ADragCannotReachAcrossTheRoomAndCancelsBackToTheFloor()
    {
        var world = PlayableSliceWorld.CreateDemo();
        var urn = world.GroundItems.Single(item => item.Name == "Clay Urn");
        var source = world.Objects.Get(urn.Id);

        var tooFar = world.BeginDrag(urn.Id);

        Assert.False(tooFar.Succeeded);
        Assert.Contains("reach", tooFar.Message);
        Assert.Null(world.HeldObject);

        WalkTo(world, world.GetGridPosition(urn.Id).Offset(0, 1));
        Assert.True(world.BeginDrag(urn.Id).Succeeded);
        Assert.True(world.CancelDrag().Succeeded);

        Assert.Equal(source, world.Objects.Get(urn.Id));
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
                Assert.True(CombatRound.Step(world, 1, 0).Succeeded);
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

            Assert.True(world.RequestSave(path).Succeeded);
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
            var move = CombatRound.Step(world, 0, step);
            Assert.True(move.Succeeded, move.Message);
        }

        while (world.PlayerPosition.X != target.X)
        {
            var step = world.PlayerPosition.X < target.X ? 1 : -1;
            var move = CombatRound.Step(world, step, 0);
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
            Assert.True(CombatRound.Step(world, 1, 0).Succeeded);
        }

        while (world.PlayerPosition.X > target.X + 1)
        {
            Assert.True(CombatRound.Step(world, -1, 0).Succeeded);
        }

        while (world.PlayerPosition.Y < target.Y)
        {
            Assert.True(CombatRound.Step(world, 0, 1).Succeeded);
        }

        while (world.PlayerPosition.Y > target.Y)
        {
            Assert.True(CombatRound.Step(world, 0, -1).Succeeded);
        }
    }
}

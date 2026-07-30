using Ash.Core;

namespace Ash.Sim.Tests;

/// <summary>
/// Carrying capacity is gear slots, not weight: <c>max(Strength, 10)</c> plus
/// any class bonus, worn gear costs nothing, and a stack costs one slot however
/// much it holds.
/// </summary>
public sealed class GearSlotTests
{
    [Theory]
    [InlineData(8, 0, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(16, 0, 16)]
    [InlineData(18, 0, 18)]
    [InlineData(16, 5, 21)]
    [InlineData(20, 5, 25)]
    [InlineData(20, 12, GearSlots.PanelCapacity)]
    public void CapacityIsStrengthOrTenPlusABonusAndNeverMoreThanThePanel(
        int strength,
        int bonus,
        int expected)
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Carrier", strength, bonus));

        Assert.Equal(expected, GearSlots.CapacityFor(strength, bonus));
        Assert.Equal(expected, store.Get(actor).CarryCapacity);
    }

    [Fact]
    public void WornGearCostsNoSlotsButTheSameItemInThePackDoes()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Fighter", strength: 10));
        var transfers = new ObjectTransferService(store);

        // Fill every slot, then wear one more thing on top.
        for (var index = 0; index < 10; index++)
        {
            store.Create(Gear($"Trinket {index}", actor));
        }

        var helmet = store.Create(Gear("Helmet", actor) with
        {
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 512, 0)),
            EquipmentSlots = EquipmentSlotMask.Head,
        });

        var worn = transfers.Execute(
            new ObjectTransferRequest(
                helmet,
                store.Get(helmet).Location,
                ObjectLocation.Equipped(actor, EquipmentSlot.Head)));

        Assert.True(worn.Succeeded, worn.Message);
        Assert.Equal(10, UsedSlots(store, actor));

        // The eleventh thing in the pack does not fit.
        var spare = store.Create(Gear("Spare", actor) with
        {
            Location = ObjectLocation.OnMap(0, new Vec3i(1024, 512, 0)),
        });
        var packed = transfers.Execute(
            new ObjectTransferRequest(
                spare,
                store.Get(spare).Location,
                ObjectLocation.InContainer(actor)));

        Assert.False(packed.Succeeded);
        Assert.Equal(
            ObjectTransferFailure.ContainerCapacity,
            packed.Failure);
        store.ValidateInvariants();
    }

    [Fact]
    public void BulkyGearCostsTwoSlots()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Porter", strength: 10));
        var transfers = new ObjectTransferService(store);
        for (var index = 0; index < 8; index++)
        {
            store.Create(Gear($"Trinket {index}", actor));
        }

        var plate = store.Create(Gear("Plate Armour", actor) with
        {
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 512, 0)),
            SlotCost = GearSlots.BulkyCost,
        });

        var carried = transfers.Execute(
            new ObjectTransferRequest(
                plate,
                store.Get(plate).Location,
                ObjectLocation.InContainer(actor)));
        Assert.True(carried.Succeeded, carried.Message);
        Assert.Equal(10, UsedSlots(store, actor));

        // Ten slots of ten used: nothing else fits, not even something small.
        var pin = store.Create(Gear("Pin", actor) with
        {
            Location = ObjectLocation.OnMap(0, new Vec3i(1024, 512, 0)),
        });
        Assert.False(
            transfers.Execute(
                new ObjectTransferRequest(
                    pin,
                    store.Get(pin).Location,
                    ObjectLocation.InContainer(actor))).Succeeded);
    }

    [Fact]
    public void AStackOfAHundredCoinsIsOneSlot()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Miser", strength: 10));
        var coins = store.Create(new ObjectSpawn
        {
            TypeId = "item.gold",
            Name = "Gold Coins",
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(actor),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item | ObjectFlags.Movable | ObjectFlags.Stackable,
            Quantity = 1,
            MaxQuantity = 100,
        });
        var stacks = new StackService(store);
        var loose = store.Create(new ObjectSpawn
        {
            TypeId = "item.gold",
            Name = "Gold Coins",
            ShapeId = "loot.generic",
            Location = ObjectLocation.OnMap(0, new Vec3i(768, 512, 0)),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item | ObjectFlags.Movable | ObjectFlags.Stackable,
            Quantity = 99,
            MaxQuantity = 100,
        });

        Assert.Equal(1, UsedSlots(store, actor));
        var merged = stacks.TransferQuantity(
            loose,
            99,
            ObjectLocation.InContainer(actor));

        Assert.True(merged.Succeeded, merged.Message);
        Assert.Equal(100, store.Get(coins).Quantity);

        // A hundred coins and one coin cost the same: one slot.
        Assert.Equal(1, UsedSlots(store, actor));
        store.ValidateInvariants();
    }

    [Fact]
    public void EveryBodySlotHoldsItsOwnThingAndNoneOfThemCostSlots()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Knight", strength: 10));
        var transfers = new ObjectTransferService(store);

        foreach (var slot in EquipmentSlots.All)
        {
            var item = store.Create(Gear($"{slot} gear", actor) with
            {
                Location = ObjectLocation.OnMap(0, new Vec3i(768, 512, 0)),
                EquipmentSlots = EquipmentSlots.MaskFor((byte)slot),
            });
            var worn = transfers.Execute(
                new ObjectTransferRequest(
                    item,
                    store.Get(item).Location,
                    ObjectLocation.Equipped(actor, slot)));
            Assert.True(worn.Succeeded, $"{slot}: {worn.Message}");
        }

        Assert.Equal(13, EquipmentSlots.All.Count);
        Assert.Equal(
            EquipmentSlots.All.Count,
            store.Enumerate().Count(value =>
                value.Location.Kind == LocationKind.Equipped));
        Assert.Equal(0, UsedSlots(store, actor));
        store.ValidateInvariants();
    }

    [Theory]
    [InlineData("item.gold", GearSlots.CoinsPerSlot, 100)]
    [InlineData("item.gem", GearSlots.GemsPerSlot, 10)]
    [InlineData("item.arrow", GearSlots.AmmunitionPerSlot, 20)]
    [InlineData("item.spike", GearSlots.SpikesPerSlot, 10)]
    [InlineData("item.ration", GearSlots.RationsPerSlot, 3)]
    public void AFullSlotOfCountedGoodsIsOneSlotAndTwoStacksAreTwo(
        string typeId,
        int perSlot,
        int expectedPerSlot)
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Carrier", strength: 10));
        Assert.Equal(expectedPerSlot, perSlot);

        store.Create(Counted(typeId, "Goods", actor, perSlot, perSlot));
        Assert.Equal(1, UsedSlots(store, actor));

        store.Create(Counted(typeId, "Goods", actor, perSlot, perSlot));
        Assert.Equal(2, UsedSlots(store, actor));

        // One short of a slotful still costs the whole slot.
        var store2 = new ObjectStore();
        using var map2 = new WorldMap(store2, 0, width: 8, depth: 8);
        var lone = store2.Create(Actor("Carrier", strength: 10));
        store2.Create(Counted(typeId, "Goods", lone, 1, perSlot));
        Assert.Equal(1, UsedSlots(store2, lone));
    }

    [Fact]
    public void CoinsOfAnyMixShareTheirSlot()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Merchant", strength: 10));

        store.Create(Coins("item.gold", "Gold Coins", actor, 50));
        store.Create(Coins("item.silver", "Silver Coins", actor, 30));
        store.Create(Coins("item.copper", "Copper Coins", actor, 20));

        // A hundred coins of any mix: one slot, not three.
        Assert.Equal(1, UsedSlots(store, actor));

        store.Create(Coins("item.copper", "Copper Coins", actor, 1));
        Assert.Equal(2, UsedSlots(store, actor));
        store.ValidateInvariants();
    }

    [Fact]
    public void AGroupAuthoredWithTwoRatesIsAContentError()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Carrier", strength: 10));
        store.Create(Coins("item.gold", "Gold Coins", actor, 10));
        var wrong = Coins("item.silver", "Silver Coins", actor, 10) with
        {
            QuantityPerSlot = 50,
            MaxQuantity = 50,
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => store.Create(wrong));

        Assert.Contains("per slot", error.Message);
    }

    [Fact]
    public void ACorpseKeepsTheCapacityTheBodyCarriedWith()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var monster = store.Create(Actor("Goblin", strength: 14) with
        {
            TypeId = "monster.goblin",
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Monster |
                ObjectFlags.Container |
                ObjectFlags.Solid,
            Health = 3,
            MaxHealth = 3,
        });
        store.Create(Gear("Loot", monster));
        Assert.Equal(14, store.Get(monster).CarryCapacity);

        store.Transform(
            monster,
            "remains.goblin",
            "Remains of Goblin",
            "container.corpse",
            ObjectFlags.Container | ObjectFlags.Corpse | ObjectFlags.Visible);

        // The body stopped being an actor but is still a container of the same
        // size, and its loot is still in it.
        Assert.Equal(14, store.Get(monster).CarryCapacity);
        Assert.Single(store.GetContents(monster));
        store.ValidateInvariants();
    }

    [Fact]
    public void SlotStateSurvivesASaveAndAVersion3SaveMigratesIntoIt()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Fighter", strength: 16, bonus: 2));
        var plate = store.Create(Gear("Plate", actor) with
        {
            SlotCost = GearSlots.BulkyCost,
        });

        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(
                ObjectWorldSave.Write(
                    ObjectWorldSave.Capture(store, "ash.test-content.v1", 1, 0)),
                "ash.test-content.v1"));

        Assert.Equal(16, loaded.Objects.Get(actor).Strength);
        Assert.Equal(2, loaded.Objects.Get(actor).GearSlotBonus);
        Assert.Equal(18, loaded.Objects.Get(actor).CarryCapacity);
        Assert.Equal(
            GearSlots.BulkyCost,
            loaded.Objects.Get(plate).SlotCost);
        Assert.Equal(store.Get(actor), loaded.Objects.Get(actor));
        loaded.Maps.Single().Dispose();
    }

    private static int UsedSlots(ObjectStore store, ObjectId container) =>
        GearSlots.UsedBy(store.GetContents(container).Select(store.Get));

    private static ObjectSpawn Actor(
        string name,
        int strength,
        int bonus = 0) =>
        new()
        {
            TypeId = "actor.test",
            Name = name,
            ShapeId = "actor.test",
            Location = ObjectLocation.OnMap(0, new Vec3i(512, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            Strength = strength,
            GearSlotBonus = bonus,
            Health = 10,
            MaxHealth = 10,
        };

    private static ObjectSpawn Counted(
        string typeId,
        string name,
        ObjectId carrier,
        int quantity,
        int perSlot,
        string group = "") =>
        new()
        {
            TypeId = typeId,
            Name = name,
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(carrier),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item | ObjectFlags.Movable | ObjectFlags.Stackable,
            Quantity = quantity,
            MaxQuantity = perSlot,
            QuantityPerSlot = perSlot,
            SlotGroup = group,
        };

    private static ObjectSpawn Coins(
        string typeId,
        string name,
        ObjectId carrier,
        int quantity) =>
        Counted(
            typeId,
            name,
            carrier,
            quantity,
            GearSlots.CoinsPerSlot,
            GearSlots.CoinGroup);

    private static ObjectSpawn Gear(string name, ObjectId carrier) =>
        new()
        {
            TypeId = $"item.{name.ToLowerInvariant().Replace(' ', '-')}",
            Name = name,
            ShapeId = "loot.generic",
            Location = ObjectLocation.InContainer(carrier),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
        };
}

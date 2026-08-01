using Ash.Core;

using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class DeathTests
{
    [Fact]
    public void ACorpseBecomesOneContainerHoldingWhatTheBodyWore()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var goblin = store.Create(Monster("Goblin", strength: 10));
        var pouchLoot = store.Create(Gear("Copper Ring", goblin));
        var blade = store.Create(Worn("Notched Blade", goblin, EquipmentSlot.RightHand));
        var helm = store.Create(Worn("Dented Helm", goblin, EquipmentSlot.Head));

        var corpse = Death.MakeCorpse(
            store,
            goblin,
            "remains.goblin",
            "Remains of Goblin",
            "container.corpse",
            ObjectFlags.Container | ObjectFlags.Corpse | ObjectFlags.Visible,
            height: 24);

        Assert.True(corpse.Succeeded, corpse.Message);
        Assert.Empty(corpse.Spilled);
        Assert.Equal([blade, helm], corpse.Stowed);

        // One container, holding what it carried and what it wore.
        Assert.Equal(
            [pouchLoot, blade, helm],
            store.GetContents(goblin).Order());
        Assert.DoesNotContain(
            store.Enumerate(),
            value => value.Location.Kind == LocationKind.Equipped);
        Assert.False(store.Get(goblin).HasFlag(ObjectFlags.Actor));
        Assert.True(store.Get(goblin).HasFlag(ObjectFlags.Corpse));
        Assert.Equal(10, store.Get(goblin).CarryCapacity);
        store.ValidateInvariants();
        map.ValidateIndex();
    }

    [Fact]
    public void GearThatWillNotFitSpillsOntoTheGround()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var brute = store.Create(Monster("Brute", strength: 10));

        // Ten slots of carried goods leaves no room for anything worn.
        for (var index = 0; index < 10; index++)
        {
            store.Create(Gear($"Trinket {index}", brute));
        }

        var blade = store.Create(Worn("Great Axe", brute, EquipmentSlot.RightHand));
        var cloak = store.Create(Worn("Ragged Cloak", brute, EquipmentSlot.Cloak));

        var corpse = Death.MakeCorpse(
            store,
            brute,
            "remains.brute",
            "Remains of Brute",
            "container.corpse",
            ObjectFlags.Container | ObjectFlags.Corpse | ObjectFlags.Visible);

        Assert.True(corpse.Succeeded, corpse.Message);
        Assert.Empty(corpse.Stowed);
        Assert.Equal([blade, cloak], corpse.Spilled);
        Assert.Contains("spill", corpse.Message);

        var body = store.Get(brute);
        foreach (var id in corpse.Spilled)
        {
            var dropped = store.Get(id);
            Assert.Equal(LocationKind.OnMap, dropped.Location.Kind);
            Assert.Equal(body.Location.Position, dropped.Location.Position);
        }

        Assert.Equal(10, store.GetContents(brute).Count);
        store.ValidateInvariants();
        map.ValidateIndex();
    }

    [Fact]
    public void AMonsterWearingGearCanBeKilledInTheDemo()
    {
        var world = PlayableSliceWorld.CreateDemo(attackResolver: new TwoHitResolver());
        var scout = world.Monsters.Single(
            monster => monster.TypeId == "monster.goblin-scout");
        var blade = world.Objects.Enumerate().Single(
            value => value.Name == "Notched Blade");
        Assert.Equal(LocationKind.Equipped, blade.Location.Kind);

        Assert.True(world.ToggleRightHand().Succeeded);
        MoveNextTo(world, world.GetGridPosition(scout.Id));
        for (var swing = 0; swing < 5 && world.Objects.Get(scout.Id).IsAlive; swing++)
        {
            // Each blow costs its own six-second round.
            CombatRound.WaitForPlayerSwing(world);
            Assert.True(world.AttackAdjacentMonster().Succeeded);
            CombatRound.WaitForPlayerImpact(world);
        }

        var corpse = world.Objects.Get(scout.Id);
        Assert.False(corpse.IsAlive);
        Assert.True(corpse.HasFlag(ObjectFlags.Corpse));
        Assert.Contains(
            world.ContentsOf(corpse.Id),
            item => item.Id == blade.Id);
        world.Objects.ValidateInvariants();
        world.Physics.ValidateInvariants();
        world.CurrentMap.ValidateIndex();
    }

    private static void MoveNextTo(PlayableSliceWorld world, GridPosition target)
    {
        while (world.PlayerPosition.Y != target.Y)
        {
            var step = world.PlayerPosition.Y < target.Y ? 1 : -1;
            Assert.True(CombatRound.Step(world, 0, step).Succeeded);
        }

        while (world.PlayerPosition.X < target.X - 1)
        {
            Assert.True(CombatRound.Step(world, 1, 0).Succeeded);
        }

        while (world.PlayerPosition.X > target.X + 1)
        {
            Assert.True(CombatRound.Step(world, -1, 0).Succeeded);
        }
    }

    private static ObjectSpawn Monster(string name, int strength) =>
        new()
        {
            TypeId = "monster.test",
            Name = name,
            ShapeId = "monster.goblin",
            Location = ObjectLocation.OnMap(0, new Vec3i(512, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 56,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Monster |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            Strength = strength,
            Health = 3,
            MaxHealth = 3,
        };

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

    private static ObjectSpawn Worn(
        string name,
        ObjectId wearer,
        EquipmentSlot slot) =>
        Gear(name, wearer) with
        {
            Location = ObjectLocation.Equipped(wearer, slot),
            EquipmentSlots = EquipmentSlots.MaskFor((byte)slot),
        };

    private sealed class TwoHitResolver : IAttackRulesResolver
    {
        public AttackResult Resolve(AttackRequest request) => new()
        {
            Hit = true,
            RawD20 = request.RawD20,
            NetRoll = request.RawD20,
            Margin = 1,
            ConcussionHits = request.AttackCategory == AttackCategoryId.OneHandedSlashing
                ? 2
                : 0,
            Mishap = false,
        };
    }
}

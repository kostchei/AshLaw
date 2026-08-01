using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

/// <summary>
/// End-to-end proofs for the shared physical-attack boundary. These compare
/// the simulation request and result with the pure rules resolver instead of
/// restating its attack-table formulas in simulation tests.
/// </summary>
public sealed class CombatAttackServiceTests
{
    private const ushort MapId = 0;
    private const ulong AttackSeed = 0xA551_2002UL;

    [Theory]
    [InlineData(ArmorType.None, 0)]
    [InlineData(ArmorType.Leather, 2)]
    [InlineData(ArmorType.Chain, 4)]
    [InlineData(ArmorType.Plate, 8)]
    public void SeededSwordAttackMatchesDirectResolverForEveryArmourCategory(
        ArmorType armor,
        int armorDefense)
    {
        var objects = new ObjectStore();
        using var map = new WorldMap(objects, MapId, width: 8, depth: 8);
        var attacker = objects.Create(Actor(
            "Sword Fighter",
            new Vec3i(256, 256, 0),
            strength: 16,
            dexterity: 12));
        objects.Create(Sword(attacker));
        var target = objects.Create(Actor(
            "Armoured Target",
            new Vec3i(384, 256, 0),
            strength: 14,
            dexterity: 14));
        if (armor != ArmorType.None)
        {
            objects.Create(Armour(target, armor, armorDefense));
        }

        var dice = new Dice(AttackSeed);
        var sheets = new ActorSheets(
            objects,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);
        var vitality = new ActorVitality(
            objects,
            RulesRepository.Vitality,
            RulesRepository.AbilityBonuses,
            dice);
        var service = new CombatAttackService(
            objects,
            sheets,
            vitality,
            dice,
            new RulesAttackRulesResolver());
        var attackerSheet = sheets.For(attacker);
        var targetSheet = sheets.For(target);
        var rawD20 = Dice.FromState(dice.State).D20();
        var expectedRequest = new AttackRequest(
            rawD20,
            AttackCategoryId.OneHandedSlashing,
            attackerSheet.AttackModifier,
            targetSheet.DefenseModifier,
            armor,
            CriticalTableId.Slash,
            AttackSize: CombatProfileCatalog.RustySword.Size,
            MaximumCriticalTier:
                CombatProfileCatalog.RustySword.MaximumCriticalTier);
        var expectedResult = AttackResolver.Resolve(
            RulesRepository.Rules,
            expectedRequest);

        var outcome = service.ResolveMelee(attacker, target);

        Assert.Equal(expectedRequest, outcome.Request);
        AssertAttackResult(expectedResult, outcome.Result);
        Assert.Equal(CombatProfileCatalog.RustySword, outcome.Profile);
    }

    [Fact]
    public void SeededCaveRatAttackUsesToothAndClawAndPunctureRules()
    {
        using var world = AdjacentDemoWorld(seed: AttackSeed);
        var rat = CaveRat(world);
        var ratSheet = world.Sheets.For(rat.Id);
        var playerSheet = world.PlayerSheet;
        var rawD20 = Dice.FromState(world.Dice.State).D20();
        var expectedRequest = new AttackRequest(
            rawD20,
            AttackCategoryId.ToothAndClaw,
            ratSheet.AttackModifier,
            playerSheet.DefenseModifier,
            playerSheet.Armor,
            CriticalTableId.Puncture,
            AttackSize: AttackSize.Small,
            MaximumCriticalTier: CriticalTier.B);
        var expectedResult = AttackResolver.Resolve(
            RulesRepository.Rules,
            expectedRequest);

        var outcome = world.Attacks.ResolveMelee(rat.Id, world.PlayerId);

        Assert.Equal(expectedRequest, outcome.Request);
        AssertAttackResult(expectedResult, outcome.Result);
        Assert.Equal(CombatProfileCatalog.CaveRatBite, outcome.Profile);
        Assert.NotEqual(
            CombatProfileCatalog.RustySword.CriticalTable,
            outcome.Request.CriticalTable);
    }

    [Fact]
    public void UnequippingPlayerWeaponChangesTheNextResolutionInTheSameWorld()
    {
        var resolver = new RecordingResolver();
        using var world = AdjacentDemoWorld(attackResolver: resolver);
        var rat = CaveRat(world);
        Assert.True(world.ToggleRightHand().Succeeded);

        var armed = world.Attacks.ResolveMelee(world.PlayerId, rat.Id);
        Assert.True(
            world.UnequipToBackpack(EquipmentSlot.RightHand).Succeeded);
        var unarmed = world.Attacks.ResolveMelee(world.PlayerId, rat.Id);

        Assert.Equal(2, resolver.Requests.Count);
        Assert.Equal(CombatProfileCatalog.RustySword, armed.Profile);
        Assert.Equal(
            AttackCategoryId.OneHandedSlashing,
            resolver.Requests[0].AttackCategory);
        Assert.Equal(CriticalTableId.Slash, resolver.Requests[0].CriticalTable);
        Assert.Equal(CombatProfileCatalog.Unarmed, unarmed.Profile);
        Assert.Equal(
            AttackCategoryId.ToothAndClaw,
            resolver.Requests[1].AttackCategory);
        Assert.Equal(
            CriticalTableId.Unbalancing,
            resolver.Requests[1].CriticalTable);
    }

    private static PlayableSliceWorld AdjacentDemoWorld(
        ulong seed = AttackSeed,
        IAttackRulesResolver? attackResolver = null)
    {
        var world = PlayableSliceWorld.CreateDemo(seed, attackResolver);
        var rat = CaveRat(world);
        var destination = rat.Location.Position with
        {
            X = rat.Location.Position.X - PlayableSliceWorld.WorldUnitsPerTile,
        };
        var moved = world.Transfers.Execute(new ObjectTransferRequest(
            world.PlayerId,
            world.Player.Location,
            ObjectLocation.OnMap(rat.Location.MapId, destination)));
        Assert.True(moved.Succeeded, moved.Message);
        return world;
    }

    private static WorldObject CaveRat(PlayableSliceWorld world) =>
        world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);

    private static ObjectSpawn Actor(
        string name,
        Vec3i position,
        int strength,
        int dexterity) =>
        new()
        {
            TypeId = $"actor.{name.ToLowerInvariant().Replace(' ', '-')}",
            Name = name,
            ShapeId = "avatar.knight",
            Location = ObjectLocation.OnMap(MapId, position),
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            Strength = strength,
            Dexterity = dexterity,
            Constitution = 14,
            Intelligence = 10,
            Wisdom = 10,
            Charisma = 10,
            Class = CharacterClass.Fighter,
            Level = 1,
            Health = 100,
            MaxHealth = 100,
            Wounds = 10,
            MaxWounds = 10,
        };

    private static ObjectSpawn Sword(ObjectId wielder) =>
        new()
        {
            TypeId = CombatProfileCatalog.RustySwordTypeId,
            Name = "Rusty Sword",
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.Equipped(
                wielder,
                EquipmentSlot.RightHand),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Weapon,
            EquipmentSlots = EquipmentSlotMask.RightHand,
            Quality = -1,
        };

    private static ObjectSpawn Armour(
        ObjectId wearer,
        ArmorType armor,
        int defenseBonus) =>
        new()
        {
            TypeId = $"item.{armor.ToString().ToLowerInvariant()}-armour",
            Name = $"{armor} Armour",
            ShapeId = "loot.armour",
            Location = ObjectLocation.Equipped(wearer, EquipmentSlot.Body),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item | ObjectFlags.Movable,
            EquipmentSlots = EquipmentSlotMask.Body,
            ArmorType = armor,
            DefenseBonus = defenseBonus,
        };

    private static void AssertAttackResult(
        AttackResult expected,
        AttackResult actual)
    {
        Assert.Equal(expected.Hit, actual.Hit);
        Assert.Equal(expected.RawD20, actual.RawD20);
        Assert.Equal(expected.NetRoll, actual.NetRoll);
        Assert.Equal(expected.Margin, actual.Margin);
        Assert.Equal(expected.ConcussionHits, actual.ConcussionHits);
        Assert.Equal(expected.CriticalTier, actual.CriticalTier);
        Assert.Equal(expected.CriticalTable, actual.CriticalTable);
        Assert.Equal(expected.TraumaIndex, actual.TraumaIndex);
        Assert.Equal(expected.TraumaText, actual.TraumaText);
        Assert.Equal(expected.TraumaEffects, actual.TraumaEffects);
        Assert.Equal(expected.Mishap, actual.Mishap);
        Assert.Equal(expected.Messages, actual.Messages);
    }

    private sealed class RecordingResolver : IAttackRulesResolver
    {
        public List<AttackRequest> Requests { get; } = [];

        public AttackResult Resolve(AttackRequest request)
        {
            Requests.Add(request);
            return new AttackResult
            {
                Hit = true,
                RawD20 = request.RawD20,
                NetRoll = request.RawD20,
                Margin = 1,
                ConcussionHits = 0,
                Mishap = false,
            };
        }
    }
}

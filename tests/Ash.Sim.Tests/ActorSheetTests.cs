using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

/// <summary>
/// Attack and defence derived from the authoritative object graph: scores and
/// class from the actor, weapon and armour from what it is actually wearing.
/// </summary>
public sealed class ActorSheetTests
{
    [Fact]
    public void AttackIsClassProgressionPlusTheWeaponsAbility()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var fighter = store.Create(Actor("Fighter", strength: 16, dexterity: 12));
        var sword = store.Create(Weapon("Longsword", fighter, finesse: false));
        var sheets = new ActorSheets(
            store,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);

        var sheet = sheets.For(fighter);

        // Fighter 1 is +1; Strength 16 is +3.
        Assert.Equal(1, sheet.ClassAttackModifier);
        Assert.Equal(3, RulesRepository.AbilityBonuses.BonusOf(
                store.Get(fighter).Abilities,
                Ability.Strength));
        Assert.Equal(4, sheet.AttackModifier);
        Assert.Equal(Ability.Strength, sheet.GoverningAbility);
        Assert.Equal(sword, sheet.Weapon);
        Assert.False(sheet.WeaponIsFinesse);
    }

    [Fact]
    public void AFinesseWeaponTakesTheBetterOfStrengthAndDexterity()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var rogue = store.Create(
            Actor("Rogue", strength: 10, dexterity: 18) with
            {
                Class = CharacterClass.Rogue,
                Level = 4,
            });
        var dagger = store.Create(Weapon("Dagger", rogue, finesse: true));
        var sheets = new ActorSheets(
            store,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);

        var sheet = sheets.For(rogue);

        Assert.Equal(Ability.Dexterity, sheet.GoverningAbility);
        Assert.Equal(4, sheet.AttackModifier - sheet.ClassAttackModifier);
        Assert.True(sheet.WeaponIsFinesse);
        Assert.Equal(dagger, sheet.Weapon);

        // The same dagger in a strong, clumsy hand goes back to strength.
        var brute = store.Create(
            Actor("Brute", strength: 18, dexterity: 8) with
            {
                Class = CharacterClass.Fighter,
                Level = 1,
            });
        store.Create(Weapon("Dagger", brute, finesse: true));
        var bruteSheet = sheets.For(brute);

        Assert.Equal(Ability.Strength, bruteSheet.GoverningAbility);
        Assert.Equal(
            4,
            bruteSheet.AttackModifier - bruteSheet.ClassAttackModifier);
    }

    [Fact]
    public void ATieOnAFinesseWeaponGoesToStrength()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Even", strength: 14, dexterity: 14));
        store.Create(Weapon("Rapier", actor, finesse: true));
        var sheets = new ActorSheets(
            store,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);

        Assert.Equal(Ability.Strength, sheets.For(actor).GoverningAbility);
    }

    [Fact]
    public void AnUnarmedActorStillHasASheet()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Brawler", strength: 14, dexterity: 10));
        var sheets = new ActorSheets(
            store,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);

        var sheet = sheets.For(actor);

        Assert.True(sheet.IsUnarmed);
        Assert.Equal(Ability.Strength, sheet.GoverningAbility);
        Assert.Equal(sheet.ClassAttackModifier + 2, sheet.AttackModifier);
    }

    [Fact]
    public void ArmourAndShieldDecideDefence()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Guard", strength: 14, dexterity: 16));
        var sheets = new ActorSheets(
            store,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);

        // Unarmoured: agility is all of it.
        Assert.Equal(3, sheets.For(actor).DefenseModifier);
        Assert.Equal(ArmorType.None, sheets.For(actor).Armor);

        store.Create(
            Weapon("Chain Hauberk", actor, finesse: false) with
            {
                Location = ObjectLocation.Equipped(actor, EquipmentSlot.Body),
                EquipmentSlots = EquipmentSlotMask.Body,
                ArmorType = ArmorType.Chain,
                DefenseBonus = 4,
                Flags = ObjectFlags.Item | ObjectFlags.Movable,
            });

        // Chain is -4 against a +2 strength bonus, so agility loses two of its
        // three points: 4 armour + 1 agility.
        var armoured = sheets.For(actor);
        Assert.Equal(ArmorType.Chain, armoured.Armor);
        Assert.Equal(5, armoured.DefenseModifier);

        store.Create(
            Weapon("Buckler", actor, finesse: false) with
            {
                Location = ObjectLocation.Equipped(actor, EquipmentSlot.LeftHand),
                EquipmentSlots = EquipmentSlotMask.LeftHand,
                DefenseBonus = 2,
                Flags = ObjectFlags.Item | ObjectFlags.Movable,
            });

        var shielded = sheets.For(actor);
        Assert.Equal(2, shielded.ShieldModifier);
        Assert.Equal(7, shielded.DefenseModifier);

        // The shield is not mistaken for the weapon.
        Assert.True(shielded.Weapon.IsNone);
    }

    [Fact]
    public void AnUnlevelledCreatureHasNoClassProgression()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var beast = store.Create(
            Actor("Cave Bear", strength: 18, dexterity: 10) with
            {
                Level = 0,
            });
        var sheets = new ActorSheets(
            store,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);

        var sheet = sheets.For(beast);

        Assert.Equal(0, sheet.ClassAttackModifier);
        Assert.Equal(4, sheet.AttackModifier);
    }

    [Fact]
    public void TheDemoAvatarHasARealSheet()
    {
        using var world = PlayableSliceWorld.CreateDemo();

        var sheet = world.PlayerSheet;

        Assert.Equal(CharacterClass.Fighter, sheet.Class);
        Assert.Equal(1, sheet.Level);
        Assert.Equal(1, sheet.ClassAttackModifier);
        Assert.Equal(12, sheet.Abilities.Strength);
        Assert.Equal(2, sheet.AttackModifier);
        Assert.True(sheet.IsUnarmed);

        // Wearing the sword changes the sheet, because the sheet is a reading
        // of the world rather than a copy of it.
        Assert.True(world.ToggleRightHand().Succeeded);
        var armed = world.PlayerSheet;
        Assert.False(armed.IsUnarmed);
        Assert.Equal("Rusty Sword", world.Objects.Get(armed.Weapon).Name);
        Assert.Equal(CombatProfileCatalog.RustySword, armed.AttackProfile);
        Assert.Equal(-1, armed.WeaponQualityModifier);
        Assert.Equal(1, armed.AttackModifier);
    }

    [Fact]
    public void BronzeDaggerKeepsWeaponAndQualityModifiersSeparate()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var rogue = store.Create(
            Actor("Rogue", strength: 10, dexterity: 18) with
            {
                Class = CharacterClass.Rogue,
                Level = 4,
            });
        store.Create(new ObjectSpawn
        {
            TypeId = CombatProfileCatalog.BronzeDaggerTypeId,
            Name = "Bronze Dagger",
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.Equipped(
                rogue,
                EquipmentSlot.RightHand),
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Weapon |
                ObjectFlags.Finesse,
            EquipmentSlots = EquipmentSlotMask.EitherHand,
            Quality = -1,
        });
        var sheet = new ActorSheets(
            store,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses).For(rogue);

        Assert.Equal(CombatProfileCatalog.BronzeDagger, sheet.AttackProfile);
        Assert.Equal(-3, sheet.WeaponAttackModifier);
        Assert.Equal(-1, sheet.WeaponQualityModifier);
        Assert.Equal(
            sheet.ClassAttackModifier + 4 - 3 - 1,
            sheet.AttackModifier);
    }

    [Fact]
    public void CaveRatUsesASmallPunctureBiteAndOthersFallBackToUnarmed()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(
            monster => monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        var goblinGuard = world.Monsters.Single(
            monster => monster.TypeId == "monster.goblin-guard");
        var goblinScout = world.Monsters.Single(
            monster => monster.TypeId == "monster.goblin-scout");

        var bite = world.Sheets.For(rat.Id).AttackProfile;
        var unarmed = world.Sheets.For(goblinGuard.Id).AttackProfile;
        var goblin = world.Sheets.For(goblinScout.Id);

        Assert.Equal(CombatProfileCatalog.CaveRatBite, bite);
        Assert.Equal(AttackCategoryId.ToothAndClaw, bite!.Category);
        Assert.Equal(CriticalTableId.Puncture, bite.CriticalTable);
        Assert.Equal(AttackSize.Small, bite.Size);
        Assert.Equal(CriticalTier.B, bite.MaximumCriticalTier);
        Assert.Equal(CombatProfileCatalog.Unarmed, unarmed);
        Assert.Equal(CriticalTier.A, unarmed!.MaximumCriticalTier);
        Assert.Equal(CombatProfileCatalog.GoblinBlade, goblin.AttackProfile);
        Assert.Equal(-1, goblin.WeaponQualityModifier);
    }

    [Fact]
    public void TheSheetSurvivesASaveAndLoad()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ash-sheet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "world.ashw");
            using var world = PlayableSliceWorld.CreateDemo();
            Assert.True(world.ToggleRightHand().Succeeded);
            var before = world.PlayerSheet;
            Assert.True(world.RequestSave(path).Succeeded);

            using var loaded = PlayableSliceWorld.Load(path);

            Assert.Equal(before, loaded.PlayerSheet);
            Assert.Equal(
                before.Abilities,
                loaded.Objects.Get(loaded.PlayerId).Abilities);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheWorldsDiceResumeWhereTheSaveLeftThem()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ash-dice-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "world.ashw");
            using var world = PlayableSliceWorld.CreateDemo();

            // Roll a few times so the state is no longer the seed.
            _ = world.Dice.Pool(7, 20);
            var state = world.Dice.State;
            var expected = Ash.Rules.Dice.FromState(state).Pool(5, 20);
            Assert.True(world.RequestSave(path).Succeeded);

            using var loaded = PlayableSliceWorld.Load(path);

            Assert.Equal(state, loaded.Dice.State);
            Assert.Equal(expected, loaded.Dice.Pool(5, 20));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ObjectSpawn Actor(
        string name,
        int strength,
        int dexterity) =>
        new()
        {
            TypeId = "actor.test",
            Name = name,
            ShapeId = "avatar.knight",
            Location = ObjectLocation.OnMap(0, new Vec3i(512, 512, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            Strength = strength,
            Dexterity = dexterity,
            Constitution = 12,
            Intelligence = 10,
            Wisdom = 10,
            Charisma = 10,
            Class = CharacterClass.Fighter,
            Level = 1,
            Health = 10,
            MaxHealth = 10,
        };

    private static ObjectSpawn Weapon(
        string name,
        ObjectId wielder,
        bool finesse) =>
        new()
        {
            TypeId = CombatProfileCatalog.RustySwordTypeId,
            Name = name,
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.Equipped(
                wielder,
                EquipmentSlot.RightHand),
            Footprint = new ObjectFootprint(32, 32),
            Height = 8,
            Flags = ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Weapon |
                (finesse ? ObjectFlags.Finesse : ObjectFlags.None),
            EquipmentSlots =
                EquipmentSlotMask.EitherHand |
                EquipmentSlotMask.Body |
                EquipmentSlotMask.LeftHand,
        };
}

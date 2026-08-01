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
    public void DemoDefensiveGearChangesTheLiveSheetWithoutRebuildingIt()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var sheets = world.Sheets;
        var jerkin = world.BackpackItems.Single(
            item => item.TypeId == "item.leather-jerkin");
        var shield = world.BackpackItems.Single(
            item => item.TypeId == "item.wooden-shield");
        var helmet = world.BackpackItems.Single(
            item => item.TypeId == "item.iron-helm");

        Assert.Equal(EquipmentSlotMask.Body, jerkin.EquipmentSlots);
        Assert.Equal(ArmorType.Leather, jerkin.ArmorType);
        Assert.Equal(2, jerkin.DefenseBonus);
        Assert.Equal(EquipmentSlotMask.LeftHand, shield.EquipmentSlots);
        Assert.Equal(2, shield.DefenseBonus);
        Assert.Equal(EquipmentSlotMask.Head, helmet.EquipmentSlots);

        var unarmoured = sheets.For(world.PlayerId);
        Assert.Equal(ArmorType.None, unarmoured.Armor);
        Assert.Equal(0, unarmoured.ShieldModifier);

        Assert.True(world.EquipFromBackpack(jerkin.Id).Succeeded);
        var armoured = sheets.For(world.PlayerId);
        Assert.Equal(ArmorType.Leather, armoured.Armor);
        Assert.Equal(unarmoured.DefenseModifier + 2, armoured.DefenseModifier);

        Assert.True(world.EquipFromBackpack(shield.Id).Succeeded);
        Assert.True(world.EquipFromBackpack(helmet.Id).Succeeded);
        var fullyEquipped = sheets.For(world.PlayerId);
        Assert.Equal(2, fullyEquipped.ShieldModifier);
        Assert.Equal(armoured.DefenseModifier + 2, fullyEquipped.DefenseModifier);
        Assert.Equal(helmet.Id, world.EquippedIn(EquipmentSlot.Head)?.Id);
    }

    [Fact]
    public void BrokenDefensiveGearSuppliesNoArmourOrDefence()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Guard", strength: 14, dexterity: 16));
        var armour = store.Create(
            Weapon("Chain Hauberk", actor, finesse: false) with
            {
                Location = ObjectLocation.Equipped(actor, EquipmentSlot.Body),
                EquipmentSlots = EquipmentSlotMask.Body,
                ArmorType = ArmorType.Chain,
                DefenseBonus = 4,
                Flags = ObjectFlags.Item | ObjectFlags.Movable,
            });
        var shield = store.Create(
            Weapon("Buckler", actor, finesse: false) with
            {
                Location = ObjectLocation.Equipped(actor, EquipmentSlot.LeftHand),
                EquipmentSlots = EquipmentSlotMask.LeftHand,
                DefenseBonus = 2,
                Flags = ObjectFlags.Item | ObjectFlags.Movable,
            });
        var sheets = new ActorSheets(
            store,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);

        Assert.Equal(ArmorType.Chain, sheets.For(actor).Armor);
        Assert.Equal(7, sheets.For(actor).DefenseModifier);

        store.BreakItem(armour);
        var brokenArmour = sheets.For(actor);
        Assert.Equal(ArmorType.None, brokenArmour.Armor);
        Assert.Equal(5, brokenArmour.DefenseModifier);

        store.BreakItem(shield);
        var brokenShield = sheets.For(actor);
        Assert.Equal(0, brokenShield.ShieldModifier);
        Assert.Equal(3, brokenShield.DefenseModifier);
    }

    [Fact]
    public void BreakingAnEquippedShieldEnablesNoShieldTrauma()
    {
        using var world = PlayableSliceWorld.CreateDemo(
            attackResolver: new NoShieldTraumaResolver());
        var shield = world.BackpackItems.Single(
            item => item.TypeId == "item.wooden-shield");
        Assert.True(world.EquipFromBackpack(shield.Id).Succeeded);
        var rat = world.Monsters.Single(
            monster => monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        var adjacent = rat.Location.Position with
        {
            X = rat.Location.Position.X - PlayableSliceWorld.WorldUnitsPerTile,
        };
        var moved = world.Transfers.Execute(new ObjectTransferRequest(
            world.PlayerId,
            world.Player.Location,
            ObjectLocation.OnMap(rat.Location.MapId, adjacent)));
        Assert.True(moved.Succeeded, moved.Message);

        var protectedOutcome = world.Attacks.ResolveMelee(rat.Id, world.PlayerId);
        Assert.Empty(protectedOutcome.ApplicableTraumaEffects);

        world.Objects.BreakItem(shield.Id);
        var exposedOutcome = world.Attacks.ResolveMelee(rat.Id, world.PlayerId);
        Assert.Contains(
            exposedOutcome.ApplicableTraumaEffects,
            effect => effect.AppliesWhen == TraumaEffectCondition.NoShield);
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
    public void EquippingDifferentWeaponsChangesTheLiveProfileAndAbility()
    {
        var store = new ObjectStore();
        using var map = new WorldMap(store, 0, width: 8, depth: 8);
        var actor = store.Create(Actor("Rogue", strength: 10, dexterity: 18));
        var sword = store.Create(Weapon("Rusty Sword", actor, finesse: false));
        var dagger = store.Create(new ObjectSpawn
        {
            TypeId = CombatProfileCatalog.BronzeDaggerTypeId,
            Name = "Bronze Dagger",
            ShapeId = "loot.shortsword",
            Location = ObjectLocation.InContainer(actor),
            Flags =
                ObjectFlags.Item |
                ObjectFlags.Movable |
                ObjectFlags.Weapon |
                ObjectFlags.Finesse,
            EquipmentSlots = EquipmentSlotMask.EitherHand,
            Quality = -1,
        });
        var sheets = new ActorSheets(
            store,
            RulesRepository.ClassProgression,
            RulesRepository.AbilityBonuses);

        var slashing = sheets.For(actor);
        Assert.Equal(sword, slashing.Weapon);
        Assert.Equal(CombatProfileCatalog.RustySword, slashing.AttackProfile);
        Assert.Equal(Ability.Strength, slashing.GoverningAbility);

        store.Move(sword, ObjectLocation.InContainer(actor));
        store.Move(dagger, ObjectLocation.Equipped(actor, EquipmentSlot.RightHand));

        var puncturing = sheets.For(actor);
        Assert.Equal(dagger, puncturing.Weapon);
        Assert.Equal(CombatProfileCatalog.BronzeDagger, puncturing.AttackProfile);
        Assert.Equal(Ability.Dexterity, puncturing.GoverningAbility);
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
    public void EveryDemoMonsterHasAValidatedNaturalOrEquippedAttack()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(
            monster => monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        var goblinGuard = world.Monsters.Single(
            monster => monster.TypeId == "monster.goblin-guard");
        var goblinScout = world.Monsters.Single(
            monster => monster.TypeId == "monster.goblin-scout");
        var tyrant = world.Monsters.Single(
            monster => monster.TypeId == "monster.many-eyed-tyrant");

        var bite = world.Sheets.For(rat.Id).AttackProfile;
        var unarmed = world.Sheets.For(goblinGuard.Id).AttackProfile;
        var goblin = world.Sheets.For(goblinScout.Id);
        var tyrantAttack = world.Sheets.For(tyrant.Id).AttackProfile;

        Assert.Equal(CombatProfileCatalog.CaveRatBite, bite);
        Assert.Equal(AttackCategoryId.ToothAndClaw, bite!.Category);
        Assert.Equal(CriticalTableId.Puncture, bite.CriticalTable);
        Assert.Equal(AttackSize.Small, bite.Size);
        Assert.Equal(CriticalTier.B, bite.MaximumCriticalTier);
        Assert.Equal(CombatProfileCatalog.Unarmed, unarmed);
        Assert.Equal(CriticalTier.A, unarmed!.MaximumCriticalTier);
        Assert.Equal(CombatProfileCatalog.Unarmed, tyrantAttack);
        Assert.Equal(CombatProfileCatalog.GoblinBlade, goblin.AttackProfile);
        Assert.Equal(-1, goblin.WeaponQualityModifier);

        Assert.All(world.Monsters, monster =>
        {
            var sheet = world.Sheets.For(monster.Id);
            var profile = Assert.IsType<AttackProfile>(sheet.AttackProfile);
            profile.Validate();
            if (sheet.Weapon.IsNone)
            {
                Assert.True(profile.IsNatural);
            }
            else
            {
                Assert.False(profile.IsNatural);
                Assert.True(CombatProfileCatalog.Default.TryWeapon(
                    world.Objects.Get(sheet.Weapon).TypeId,
                    out var equipped));
                Assert.Equal(profile, equipped);
            }
        });
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

    private sealed class NoShieldTraumaResolver : IAttackRulesResolver
    {
        public AttackResult Resolve(AttackRequest request) => new()
        {
            Hit = true,
            RawD20 = request.RawD20,
            NetRoll = request.RawD20,
            Margin = 1,
            ConcussionHits = 0,
            Mishap = false,
            TraumaEffects =
            [
                new TraumaEffect(
                    TraumaEffectKind.AdditionalHits,
                    Magnitude: 1,
                    AppliesWhen: TraumaEffectCondition.NoShield),
            ],
        };
    }
}

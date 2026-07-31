using Ash.Core;
using Ash.Rules;

namespace Ash.Sim.Tests;

/// <summary>
/// The seam between the store's numbers and the injury rules' meaning: nothing
/// else in the simulation hurts or heals a body, and what it writes has to
/// survive a save.
/// </summary>
public sealed class ActorVitalityTests
{
    private const string Fingerprint = "ash.test-content.v1";

    private static VitalityData Data => RulesRepository.Vitality;

    private static AbilityBonusTable Bonuses => RulesRepository.AbilityBonuses;

    [Fact]
    public void ARolledBodyIsHitDicePerLevelOverLevelPlusTheBestBonus()
    {
        var body = ActorVitality.RollBody(
            Data,
            Bonuses,
            new Dice(2026),
            CharacterClass.Fighter,
            level: 3,
            new AbilityScores(16, 12, 14, 10, 10, 10));

        Assert.Equal(3, body.Concussion.Dice.Count);
        Assert.Equal(10, body.Concussion.DieSides);

        // Level 3 plus the +3 from a 16: the best of the six, whichever it is.
        Assert.Equal(6, body.MaximumWounds);
    }

    [Fact]
    public void DamageSpillsFromHitsIntoWoundsAndOntoTheClock()
    {
        var (store, actor, vitality) = Fixture(concussion: 3, wounds: 2);

        Assert.Equal(1, vitality.Damage(actor, 2).State.Concussion);
        Assert.True(store.Get(actor).IsAlive);

        var wounding = vitality.Damage(actor, 2);
        Assert.Equal(0, wounding.State.Concussion);
        Assert.Equal(1, wounding.State.Wounds);
        Assert.Equal(1, wounding.WoundsLost);

        var dropped = vitality.Damage(actor, 1);
        Assert.True(dropped.EnteredDeathClock);
        Assert.True(store.Get(actor).Injury.IsOnTheDeathClock);

        // Still alive: the clock has not spoken yet.
        Assert.True(store.Get(actor).IsAlive);
        store.ValidateInvariants();
    }

    [Fact]
    public void AMonsterWithNoWoundLayerDiesWhenItsHitsRunOut()
    {
        var (store, monster, vitality) = Fixture(concussion: 2, wounds: 0);

        var killed = vitality.Damage(monster, 2);

        Assert.True(killed.State.IsDead);
        Assert.False(store.Get(monster).IsAlive);
        store.ValidateInvariants();
    }

    [Fact]
    public void TheStoreRefusesAnInjuryStateBuiltForAnotherBody()
    {
        var (store, actor, _) = Fixture(concussion: 5, wounds: 2);

        Assert.Throws<InvalidOperationException>(
            () => store.SetInjury(actor, InjuryState.Whole(99, 2)));
    }

    [Fact]
    public void TheWholeInjuryStateSurvivesASaveAndLoad()
    {
        var (store, actor, vitality) = Fixture(concussion: 4, wounds: 3);
        using var map = new WorldMap(store, 0, width: 4, depth: 4);

        vitality.Damage(actor, 6);
        var before = store.Get(actor).Injury;
        Assert.Equal(1, before.Wounds);
        Assert.NotEqual(AbilityMask.None, before.Impairments);

        var loaded = ObjectWorldSave.Restore(
            ObjectWorldSave.Read(
                ObjectWorldSave.Write(
                    ObjectWorldSave.Capture(
                        store,
                        Fingerprint,
                        simulationTick: 7,
                        currentMapId: 0)),
                Fingerprint));

        Assert.Equal(before, loaded.Objects.Get(actor).Injury);
        loaded.Objects.ValidateInvariants();
        foreach (var loadedMap in loaded.Maps)
        {
            loadedMap.Dispose();
        }
    }

    private static (ObjectStore Store, ObjectId Actor, ActorVitality Vitality)
        Fixture(int concussion, int wounds)
    {
        var store = new ObjectStore();
        var actor = store.Create(new ObjectSpawn
        {
            TypeId = "actor.test",
            Name = "Test Body",
            ShapeId = "avatar.knight",
            Location = ObjectLocation.OnMap(0, new Vec3i(256, 256, 0)),
            Footprint = new ObjectFootprint(128, 128),
            Height = 64,
            Flags =
                ObjectFlags.Actor |
                ObjectFlags.Container |
                ObjectFlags.Solid |
                ObjectFlags.Visible,
            Strength = 12,
            Dexterity = 12,
            Constitution = 14,
            Intelligence = 10,
            Wisdom = 10,
            Charisma = 10,
            Class = CharacterClass.Fighter,
            Level = 2,
            Health = concussion,
            MaxHealth = concussion,
            Wounds = wounds,
            MaxWounds = wounds,
        });

        return (
            store,
            actor,
            new ActorVitality(store, Data, Bonuses, new Dice(31337)));
    }
}

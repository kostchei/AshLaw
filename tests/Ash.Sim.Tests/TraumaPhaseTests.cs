using Ash.Rules;

namespace Ash.Sim.Tests;

public sealed class TraumaPhaseTests
{
    private const int PhysicsTicksPerBeat = 12;

    [Fact]
    public void ConditionsResumeWithExactTimingAfterSaveAndLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ash-conditions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "world.ashw");
            using var world = PlayableSliceWorld.CreateDemo();
            world.Conditions.Apply(
                world.PlayerId,
                ObjectId.None,
                new TraumaEffect(
                    TraumaEffectKind.Stun,
                    Duration: 2,
                    DurationUnit: TraumaDurationUnit.Rounds),
                world.Clock.Tick);
            var expected = Assert.Single(world.Conditions.Capture());
            Assert.True(world.RequestSave(path).Succeeded);

            using var loaded = PlayableSliceWorld.Load(path);
            var restored = Assert.Single(loaded.Conditions.Capture());

            Assert.Equal(expected, restored);
            Assert.True(loaded.Conditions.PreventsAction(loaded.PlayerId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void APeriodicTickResumesRatherThanRestartingAfterLoad()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ash-bleed-save-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "world.ashw");
            using var world = PlayableSliceWorld.CreateDemo();
            world.Conditions.Apply(
                world.PlayerId,
                ObjectId.None,
                new TraumaEffect(TraumaEffectKind.Bleeding, Magnitude: 1),
                world.Clock.Tick);
            AdvanceBeats(world, 10);
            var health = world.PlayerHealth;
            Assert.True(world.RequestSave(path).Succeeded);

            using var loaded = PlayableSliceWorld.Load(path);
            AdvanceBeats(loaded, ConditionTiming.BeatsPerRound - 10 - 1);
            Assert.Equal(health, loaded.PlayerHealth);
            AdvanceBeats(loaded, 1);
            Assert.Equal(health - 1, loaded.PlayerHealth);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BleedingDealsDamageOnExactRoundBeats()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var start = world.PlayerHealth;
        world.Conditions.Apply(
            world.PlayerId,
            ObjectId.None,
            new TraumaEffect(TraumaEffectKind.Bleeding, Magnitude: 1),
            world.Clock.Tick);

        AdvanceBeats(world, ConditionTiming.BeatsPerRound - 1);
        Assert.Equal(start, world.PlayerHealth);
        AdvanceBeats(world, 1);
        Assert.Equal(start - 1, world.PlayerHealth);
        AdvanceBeats(world, ConditionTiming.BeatsPerRound);
        Assert.Equal(start - 2, world.PlayerHealth);
    }

    [Fact]
    public void DispatcherPolicyListMatchesEveryStructuredTraumaKind()
    {
        Assert.Equal(
            Enum.GetValues<TraumaEffectKind>().Order(),
            TraumaEffectDispatcher.SupportedKinds.Order());
    }

    [Theory]
    [InlineData(TraumaEffectKind.Bleeding)]
    [InlineData(TraumaEffectKind.ActivityPenalty)]
    [InlineData(TraumaEffectKind.Stun)]
    [InlineData(TraumaEffectKind.Prone)]
    [InlineData(TraumaEffectKind.Unconscious)]
    [InlineData(TraumaEffectKind.BreakBone)]
    [InlineData(TraumaEffectKind.DisableLimb)]
    [InlineData(TraumaEffectKind.DestroyEye)]
    [InlineData(TraumaEffectKind.Paralyzed)]
    [InlineData(TraumaEffectKind.Restrained)]
    [InlineData(TraumaEffectKind.Incapacitated)]
    [InlineData(TraumaEffectKind.Suffocating)]
    [InlineData(TraumaEffectKind.Exhaustion)]
    [InlineData(TraumaEffectKind.Injured)]
    [InlineData(TraumaEffectKind.Vex)]
    [InlineData(TraumaEffectKind.Sap)]
    [InlineData(TraumaEffectKind.Slow)]
    public void EveryDurableDispatcherEffectCommitsItsNamedCondition(
        TraumaEffectKind kind)
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(world, rat);
        var durationUnit = kind switch
        {
            TraumaEffectKind.DestroyEye => TraumaDurationUnit.Permanent,
            TraumaEffectKind.BreakBone or TraumaEffectKind.DisableLimb or
                TraumaEffectKind.Paralyzed or TraumaEffectKind.Exhaustion or
                TraumaEffectKind.Injured or TraumaEffectKind.Bleeding =>
                TraumaDurationUnit.UntilHealed,
            TraumaEffectKind.Sap or TraumaEffectKind.Vex => TraumaDurationUnit.None,
            _ => TraumaDurationUnit.Rounds,
        };
        var effect = new TraumaEffect(
            kind,
            Magnitude: 1,
            Duration: durationUnit == TraumaDurationUnit.Rounds ? 1 : 0,
            DurationUnit: durationUnit,
            Detail: "lower leg");

        world.Trauma.Apply(rat.Id, world.PlayerId, [effect]);

        var committed = Assert.Single(world.Conditions.Of(world.PlayerId));
        Assert.Equal(kind, committed.Kind);
        Assert.Equal(rat.Id, committed.SourceId);
        Assert.Equal(1, committed.Magnitude);
        Assert.Equal(ActorConditionService.RemovalPolicyFor(effect), committed.RemovalPolicy);
        Assert.Equal(ActorConditionService.StackingPolicyFor(kind), committed.StackingPolicy);
        Assert.Equal($"condition.{kind.ToString().ToLowerInvariant()}", committed.PresentationKey);
    }

    [Fact]
    public void PersistentInjuriesCommitDistinctMechanicalPenalties()
    {
        var resolver = new RecordingMissResolver();
        using var world = PlayableSliceWorld.CreateDemo(attackResolver: resolver);
        var rat = world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(world, rat);
        var baseline = world.PlayerSheet.AttackModifier;

        world.Trauma.Apply(
            rat.Id,
            world.PlayerId,
            [
                new TraumaEffect(
                    TraumaEffectKind.BreakBone,
                    Magnitude: 1,
                    DurationUnit: TraumaDurationUnit.UntilHealed,
                    Detail: "weapon arm"),
                new TraumaEffect(
                    TraumaEffectKind.DisableLimb,
                    DurationUnit: TraumaDurationUnit.UntilHealed,
                    Detail: "shield arm"),
                new TraumaEffect(
                    TraumaEffectKind.DestroyEye,
                    Magnitude: 1,
                    DurationUnit: TraumaDurationUnit.Permanent,
                    Detail: "left eye"),
                new TraumaEffect(
                    TraumaEffectKind.Exhaustion,
                    Magnitude: 2,
                    DurationUnit: TraumaDurationUnit.UntilHealed),
                new TraumaEffect(
                    TraumaEffectKind.Injured,
                    Magnitude: 1,
                    DurationUnit: TraumaDurationUnit.UntilHealed),
            ]);

        _ = world.Attacks.ResolveMelee(world.PlayerId, rat.Id);

        Assert.Equal(baseline - 13, Assert.Single(resolver.Requests).AttackModifier);
        Assert.Equal(1, world.Conditions.MovementStepsPerRound(world.PlayerId, 6));
        Assert.Equal(
            ActorConditionRemovalPolicy.Permanent,
            Assert.Single(world.Conditions.Of(world.PlayerId).Where(condition =>
                condition.Kind == TraumaEffectKind.DestroyEye)).RemovalPolicy);
        var healed = world.Conditions.RemoveHealed(world.PlayerId);
        Assert.Equal(4, healed);
        Assert.True(world.Conditions.Has(world.PlayerId, TraumaEffectKind.DestroyEye));
    }

    [Fact]
    public void GrazeUsesTheGoverningAbilityModifierAndMultiplier()
    {
        using var world = PlayableSliceWorld.CreateDemo(
            attackResolver: new GrazeMissResolver(multiplier: 2));
        var rat = world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(world, rat);
        var sheet = world.PlayerSheet;
        var abilityModifier = Math.Max(0, RulesRepository.AbilityBonuses.BonusOf(
            sheet.Abilities,
            sheet.GoverningAbility));
        var before = world.Objects.Get(rat.Id).Health;

        var outcome = world.Attacks.ResolveMelee(world.PlayerId, rat.Id);

        Assert.False(outcome.Hit);
        Assert.Equal(abilityModifier * 2, outcome.ImmediateHits);
        Assert.Equal(before - abilityModifier * 2, world.Objects.Get(rat.Id).Health);
        Assert.Equal(abilityModifier * 2, outcome.Trauma.AdditionalDamage);
    }

    [Fact]
    public void StableAtZeroRollsD4HoursRecoversOnTheExactBeatAndStaysInjured()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(world, rat);
        var predictor = Dice.FromState(world.Dice.State);
        var rolledHours = predictor.Roll(4);

        world.Trauma.Apply(
            rat.Id,
            world.PlayerId,
            [
                new TraumaEffect(
                    TraumaEffectKind.StableAtZero,
                    Magnitude: 1,
                    Duration: 1,
                    DurationUnit: TraumaDurationUnit.D4Hours),
                new TraumaEffect(
                    TraumaEffectKind.Injured,
                    Magnitude: 1,
                    DurationUnit: TraumaDurationUnit.UntilHealed),
            ]);

        Assert.Equal(VitalityState.Stable, world.PlayerInjury.State);
        Assert.Equal(0, world.PlayerInjury.Concussion);
        Assert.Equal(0, world.PlayerInjury.Wounds);
        var stable = Assert.Single(world.Conditions.Of(world.PlayerId).Where(condition =>
            condition.Kind == TraumaEffectKind.StableAtZero));
        var recoveryTick = world.Clock.Tick + rolledHours * ConditionTiming.BeatsPerHour;
        Assert.Equal(recoveryTick, stable.ExpiresAtTick);

        var early = world.Conditions.AdvanceTo(recoveryTick - 1);
        Assert.Empty(early.Recoveries);
        var due = world.Conditions.AdvanceTo(recoveryTick);
        var recovery = Assert.Single(due.Recoveries);
        _ = world.Vitality.RecoverStableAtZero(recovery.ActorId, recovery.RestoredHits);

        Assert.Equal(VitalityState.Standing, world.PlayerInjury.State);
        Assert.Equal(1, world.PlayerInjury.Concussion);
        Assert.Equal(0, world.PlayerInjury.Wounds);
        Assert.False(world.Conditions.Has(world.PlayerId, TraumaEffectKind.StableAtZero));
        Assert.True(world.Conditions.Has(world.PlayerId, TraumaEffectKind.Injured));
    }

    [Fact]
    public void CleaveOnlyGrantsAndConsumesAgainstALegalSecondTarget()
    {
        using var world = PlayableSliceWorld.CreateDemo(
            attackResolver: new CleaveResolver());
        var rat = world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        var scout = world.Monsters.Single(monster =>
            monster.TypeId == "monster.goblin-scout");
        PlaceBeside(world, rat);
        MoveTo(world, scout, rat.Location.Position with
        {
            Y = rat.Location.Position.Y + PlayableSliceWorld.WorldUnitsPerTile,
        });

        Assert.True(world.Combat.TryPlayerAttack(rat.Id).Swung);
        AdvanceBeats(world, CombatDirector.AlertToActionBeats);

        Assert.True(world.Conditions.Has(
            world.PlayerId, TraumaEffectKind.Cleave, rat.Id));
        Assert.False(world.Attacks.IsLegalCleaveFollowUp(world.PlayerId, rat.Id));
        Assert.True(world.Attacks.IsLegalCleaveFollowUp(world.PlayerId, scout.Id));
        var refused = world.Combat.TryPlayerAttack(rat.Id);
        Assert.False(refused.Swung);
        Assert.Contains("different creature", refused.Message);

        Assert.True(world.Combat.TryPlayerAttack(scout.Id).Swung);
        AdvanceBeats(world, CombatDirector.AlertToActionBeats);

        Assert.False(world.Conditions.Has(world.PlayerId, TraumaEffectKind.Cleave));
    }

    [Fact]
    public void FailedStableMutationRestoresTheD4RollAndBodyState()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(world, rat);
        var beforeDice = world.Dice.State;
        var beforeInjury = world.PlayerInjury;
        var beforeConditions = world.Conditions.Capture();

        Assert.Throws<InjectedCombatMutationException>(() => world.Trauma.Apply(
            rat.Id,
            world.PlayerId,
            [new TraumaEffect(
                TraumaEffectKind.StableAtZero,
                Magnitude: 1,
                Duration: 1,
                DurationUnit: TraumaDurationUnit.D4Hours)],
            afterStage: stage =>
            {
                if (stage == CombatMutationStage.Conditions)
                {
                    throw new InjectedCombatMutationException(stage);
                }
            }));

        Assert.Equal(beforeDice, world.Dice.State);
        Assert.Equal(beforeInjury, world.PlayerInjury);
        Assert.Equal(beforeConditions, world.Conditions.Capture());
    }

    [Fact]
    public void DyingAndUnconsciousDoNotContradictAuthoritativeVitality()
    {
        using var dyingWorld = PlayableSliceWorld.CreateDemo();
        var dyingRat = dyingWorld.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(dyingWorld, dyingRat);
        dyingWorld.Trauma.Apply(
            dyingRat.Id,
            dyingWorld.PlayerId,
            [new TraumaEffect(TraumaEffectKind.Dying)]);

        Assert.Equal(VitalityState.Dying, dyingWorld.PlayerInjury.State);
        Assert.False(ActorConditionService.CanStore(TraumaEffectKind.Dying));
        Assert.False(dyingWorld.Conditions.Has(
            dyingWorld.PlayerId, TraumaEffectKind.Dying));
        Assert.Throws<InvalidOperationException>(() =>
            dyingWorld.Attacks.ResolveMelee(dyingWorld.PlayerId, dyingRat.Id));

        using var unconsciousWorld = PlayableSliceWorld.CreateDemo();
        var unconsciousRat = unconsciousWorld.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(unconsciousWorld, unconsciousRat);
        unconsciousWorld.Trauma.Apply(
            unconsciousRat.Id,
            unconsciousWorld.PlayerId,
            [new TraumaEffect(
                TraumaEffectKind.Unconscious,
                Duration: 1,
                DurationUnit: TraumaDurationUnit.Hours)]);

        Assert.Equal(VitalityState.Standing, unconsciousWorld.PlayerInjury.State);
        Assert.True(unconsciousWorld.Conditions.PreventsAction(
            unconsciousWorld.PlayerId));
        Assert.True(unconsciousWorld.Conditions.PreventsMovement(
            unconsciousWorld.PlayerId));
    }

    [Fact]
    public void CriticalNarrativeImmediateDamageAndPersistentStateShareOneResult()
    {
        using var world = PlayableSliceWorld.CreateDemo(
            attackResolver: new NarratedCriticalResolver());
        var rat = world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(world, rat);
        var before = rat.Health;

        var outcome = world.Attacks.ResolveMelee(world.PlayerId, rat.Id);

        Assert.Equal(CriticalTier.C, outcome.Result.CriticalTier);
        Assert.Equal("Arm smashed; the rat reels.", outcome.Result.TraumaText);
        Assert.Equal(3, outcome.ImmediateHits);
        Assert.Equal(before - 3, world.Objects.Get(rat.Id).Health);
        Assert.Contains(outcome.ApplicableTraumaEffects, effect =>
            effect.Kind == TraumaEffectKind.AdditionalHits && effect.Magnitude == 2);
        Assert.True(world.Conditions.Has(rat.Id, TraumaEffectKind.Injured));
    }

    [Fact]
    public void ForcedMovementUsesSpatialTransferAndBrokenWeaponsStopBeingWeapons()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var rat = world.Monsters.Single(monster =>
            monster.TypeId == CombatProfileCatalog.CaveRatTypeId);
        PlaceBeside(world, rat);
        var before = world.PlayerPosition;

        var pushed = world.Trauma.Apply(
            rat.Id,
            world.PlayerId,
            [new TraumaEffect(TraumaEffectKind.ForcedMovement, Magnitude: 5)]);

        Assert.True(pushed.Moved);
        Assert.Equal(1, before.ManhattanDistance(world.PlayerPosition));

        var pushedAgain = world.Trauma.Apply(
            rat.Id,
            world.PlayerId,
            [new TraumaEffect(TraumaEffectKind.Push, Magnitude: 5)]);
        Assert.True(pushedAgain.Moved);
        world.Trauma.Apply(
            rat.Id,
            world.PlayerId,
            [new TraumaEffect(TraumaEffectKind.Topple)]);
        Assert.True(world.Conditions.Has(world.PlayerId, TraumaEffectKind.Prone));

        Assert.True(world.ToggleRightHand().Succeeded);
        var weapon = world.PlayerSheet.Weapon;
        Assert.False(weapon.IsNone);
        var broken = world.Trauma.Apply(
            rat.Id,
            world.PlayerId,
            [new TraumaEffect(TraumaEffectKind.BreakItem, Detail: "Sword")]);

        Assert.Contains(weapon, broken.BrokenItems);
        Assert.True(world.Objects.Get(weapon).HasFlag(ObjectFlags.Broken));
        Assert.True(world.PlayerSheet.IsUnarmed);

        var scout = world.Monsters.Single(monster => monster.TypeId == "monster.goblin-scout");
        var blade = world.Objects.Enumerate().Single(item => item.Name == "Notched Blade");
        var dropped = world.Trauma.Apply(
            rat.Id,
            scout.Id,
            [new TraumaEffect(TraumaEffectKind.DropHeldItem, Detail: "Blade")]);
        Assert.Contains(blade.Id, dropped.DroppedItems);
        Assert.Equal(LocationKind.OnMap, world.Objects.Get(blade.Id).Location.Kind);
    }

    [Fact]
    public void LethalMultiEffectTraumaPublishesOneCoherentCorpseCommit()
    {
        using var world = PlayableSliceWorld.CreateDemo(
            attackResolver: new LethalCompositeResolver());
        var scout = world.Monsters.Single(monster => monster.TypeId == "monster.goblin-scout");
        PlaceBeside(world, scout);
        var blade = world.Objects.Enumerate().Single(item => item.Name == "Notched Blade");
        var commits = 0;
        var revision = world.CurrentMap.Revision;
        world.Objects.Committed += commit =>
        {
            commits++;
            var corpse = world.Objects.Get(scout.Id);
            Assert.True(corpse.HasFlag(ObjectFlags.Corpse));
            Assert.False(corpse.HasFlag(ObjectFlags.Actor));
            Assert.True(world.Objects.Get(blade.Id).HasFlag(ObjectFlags.Broken));
            Assert.NotEqual(LocationKind.Equipped, world.Objects.Get(blade.Id).Location.Kind);
            Assert.Empty(world.Conditions.Of(scout.Id));
            Assert.False(world.Conditions.Has(world.PlayerId, TraumaEffectKind.Cleave));
        };

        var result = world.Attacks.ResolveMelee(world.PlayerId, scout.Id);

        Assert.Equal(CriticalTier.E, result.Result.CriticalTier);
        Assert.True(result.Trauma.Moved);
        Assert.Contains(blade.Id, result.Trauma.DroppedItems);
        Assert.True(result.Trauma.CorpseCreated);
        Assert.False(result.Trauma.CleaveGranted);
        Assert.Equal(1, commits);
        Assert.Equal(revision + 1, world.CurrentMap.Revision);
        Assert.Empty(world.Conditions.Of(scout.Id));
        world.CurrentMap.ValidateIndex();
        world.Objects.ValidateInvariants();
    }

    [Theory]
    [InlineData(CombatMutationStage.Injury)]
    [InlineData(CombatMutationStage.Transfers)]
    [InlineData(CombatMutationStage.Physics)]
    [InlineData(CombatMutationStage.Items)]
    [InlineData(CombatMutationStage.Transform)]
    [InlineData(CombatMutationStage.Conditions)]
    public void FailureAfterEveryMutationStageRestoresTheWholeWorld(
        CombatMutationStage failedStage)
    {
        using var world = PlayableSliceWorld.CreateDemo();
        var scout = world.Monsters.Single(monster =>
            monster.TypeId == "monster.goblin-scout");
        PlaceBeside(world, scout);
        var blade = world.Objects.Enumerate().Single(item =>
            item.Name == "Notched Blade");
        world.Conditions.Apply(
            scout.Id,
            world.PlayerId,
            new TraumaEffect(TraumaEffectKind.Injured, Magnitude: 1),
            world.Clock.Tick);
        var beforeObjects = world.Objects.Enumerate().ToArray();
        var beforeConditions = world.Conditions.Capture();
        var beforeRevision = world.CurrentMap.Revision;
        var commits = 0;
        world.Objects.Committed += _ => commits++;

        Assert.Throws<InjectedCombatMutationException>(() => world.Trauma.Apply(
            world.PlayerId,
            scout.Id,
            [
                new TraumaEffect(TraumaEffectKind.Stun, Duration: 1,
                    DurationUnit: TraumaDurationUnit.Rounds),
                new TraumaEffect(TraumaEffectKind.BreakItem, Detail: "Blade"),
                new TraumaEffect(TraumaEffectKind.ForcedMovement, Magnitude: 5),
                new TraumaEffect(TraumaEffectKind.Cleave),
                new TraumaEffect(TraumaEffectKind.Death),
            ],
            afterStage: stage =>
            {
                if (stage == failedStage)
                {
                    throw new InjectedCombatMutationException(stage);
                }
            }));

        Assert.Equal(beforeObjects, world.Objects.Enumerate());
        Assert.Equal(beforeConditions, world.Conditions.Capture());
        Assert.Equal(beforeRevision, world.CurrentMap.Revision);
        Assert.Equal(0, commits);
        Assert.True(world.Objects.Get(scout.Id).HasFlag(ObjectFlags.Actor));
        Assert.False(world.Objects.Get(scout.Id).HasFlag(ObjectFlags.Corpse));
        Assert.Equal(LocationKind.Equipped, world.Objects.Get(blade.Id).Location.Kind);
        Assert.False(world.Objects.Get(blade.Id).HasFlag(ObjectFlags.Broken));
        world.CurrentMap.ValidateIndex();
        world.Objects.ValidateInvariants();
    }

    [Fact]
    public void FailedCompositeMutationRollsBackInjuryEquipmentAndPublication()
    {
        using var world = PlayableSliceWorld.CreateDemo();
        Assert.True(world.ToggleRightHand().Succeeded);
        var weapon = world.PlayerSheet.Weapon;
        var beforeInjury = world.PlayerInjury;
        var beforeWeapon = world.Objects.Get(weapon);
        var damaged = Injury.Damage(beforeInjury, 1, new Dice(1)).State;
        var commits = 0;
        var revision = world.CurrentMap.Revision;
        world.Objects.Committed += _ => commits++;

        var staleSource = ObjectLocation.InContainer(world.PlayerId);
        Assert.Throws<ObjectTransferException>(() =>
            world.Objects.CommitCombatMutation(new CombatMutation(
                world.PlayerId,
                damaged,
                [new ObjectTransferRequest(weapon, staleSource, world.Player.Location)],
                [weapon])));

        Assert.Throws<InvalidObjectIdException>(() =>
            world.Objects.CommitCombatMutation(new CombatMutation(
                world.PlayerId,
                damaged,
                BreakItems: [ObjectId.None])));

        Assert.Throws<InvalidOperationException>(() =>
            world.Objects.CommitCombatMutation(new CombatMutation(
                world.PlayerId,
                damaged,
                Transform: new CombatTransform(
                    world.PlayerId,
                    world.Player.TypeId,
                    world.Player.Name,
                    world.Player.ShapeId,
                    world.Player.Flags & ~ObjectFlags.Actor))));

        Assert.Throws<InvalidOperationException>(() =>
            world.Objects.CommitCombatMutation(new CombatMutation(
                world.PlayerId,
                damaged,
                Conditions:
                [
                    new ActorConditionMutation(
                        world.PlayerId,
                        ObjectId.None,
                        new TraumaEffect(TraumaEffectKind.AdditionalHits),
                        world.Clock.Tick),
                ])));

        Assert.Equal(beforeInjury, world.PlayerInjury);
        Assert.Equal(beforeWeapon, world.Objects.Get(weapon));
        Assert.Equal(0, commits);
        Assert.Equal(revision, world.CurrentMap.Revision);
        world.CurrentMap.ValidateIndex();
        world.Objects.ValidateInvariants();
    }

    private static void PlaceBeside(PlayableSliceWorld world, WorldObject rat)
    {
        var destination = rat.Location.Position with
        {
            X = rat.Location.Position.X - PlayableSliceWorld.WorldUnitsPerTile,
        };
        var transfer = world.Transfers.Execute(new ObjectTransferRequest(
            world.PlayerId,
            world.Player.Location,
            ObjectLocation.OnMap(rat.Location.MapId, destination)));
        Assert.True(transfer.Succeeded, transfer.Message);
    }

    private static void MoveTo(
        PlayableSliceWorld world,
        WorldObject actor,
        Ash.Core.Vec3i destination)
    {
        var transfer = world.Transfers.Execute(new ObjectTransferRequest(
            actor.Id,
            actor.Location,
            ObjectLocation.OnMap(actor.Location.MapId, destination)));
        Assert.True(transfer.Succeeded, transfer.Message);
    }

    private static void AdvanceBeats(PlayableSliceWorld world, int beats)
    {
        for (var beat = 0; beat < beats; beat++)
        {
            var start = world.Clock.Tick;
            for (var tick = 0; tick <= PhysicsTicksPerBeat && world.Clock.Tick == start; tick++)
            {
                world.AdvancePhysics();
            }
        }
    }

    private sealed class InjectedCombatMutationException(CombatMutationStage stage)
        : Exception($"Injected failure after {stage}.");

    private sealed class LethalCompositeResolver : IAttackRulesResolver
    {
        public AttackResult Resolve(AttackRequest request) => new()
        {
            Hit = true,
            RawD20 = request.RawD20,
            NetRoll = request.RawD20,
            Margin = 20,
            ConcussionHits = 1,
            CriticalTier = Ash.Rules.CriticalTier.E,
            CriticalTable = Ash.Rules.CriticalTableId.Slash,
            TraumaText = "A lethal composite critical.",
            TraumaEffects =
            [
                new TraumaEffect(
                    TraumaEffectKind.Stun,
                    Duration: 1,
                    DurationUnit: TraumaDurationUnit.Rounds),
                new TraumaEffect(TraumaEffectKind.DropHeldItem, Detail: "Blade"),
                new TraumaEffect(TraumaEffectKind.BreakItem, Detail: "Blade"),
                new TraumaEffect(TraumaEffectKind.ForcedMovement, Magnitude: 5),
                new TraumaEffect(TraumaEffectKind.Cleave),
                new TraumaEffect(TraumaEffectKind.Death),
            ],
            Mishap = false,
        };
    }

    private sealed class RecordingMissResolver : IAttackRulesResolver
    {
        public List<AttackRequest> Requests { get; } = [];

        public AttackResult Resolve(AttackRequest request)
        {
            Requests.Add(request);
            return new AttackResult
            {
                Hit = false,
                RawD20 = request.RawD20,
                NetRoll = request.RawD20,
                Margin = -1,
                ConcussionHits = 0,
                Mishap = false,
            };
        }
    }

    private sealed class GrazeMissResolver(int multiplier) : IAttackRulesResolver
    {
        public AttackResult Resolve(AttackRequest request) => new()
        {
            Hit = false,
            RawD20 = request.RawD20,
            NetRoll = request.RawD20,
            Margin = -1,
            ConcussionHits = 0,
            TraumaText = $"Graze x{multiplier}.",
            TraumaEffects =
            [
                new TraumaEffect(TraumaEffectKind.Graze, Magnitude: multiplier),
            ],
            Mishap = false,
        };
    }

    private sealed class CleaveResolver : IAttackRulesResolver
    {
        private int _calls;

        public AttackResult Resolve(AttackRequest request)
        {
            _calls++;
            return new AttackResult
            {
                Hit = true,
                RawD20 = request.RawD20,
                NetRoll = request.RawD20,
                Margin = 1,
                ConcussionHits = 0,
                TraumaText = _calls == 1 ? "Cleave." : null,
                TraumaEffects = _calls == 1
                    ? [new TraumaEffect(TraumaEffectKind.Cleave)]
                    : [],
                Mishap = false,
            };
        }
    }

    private sealed class NarratedCriticalResolver : IAttackRulesResolver
    {
        public AttackResult Resolve(AttackRequest request) => new()
        {
            Hit = true,
            RawD20 = request.RawD20,
            NetRoll = request.RawD20,
            Margin = 10,
            ConcussionHits = 1,
            CriticalTier = CriticalTier.C,
            CriticalTable = CriticalTableId.Crush,
            TraumaText = "Arm smashed; the rat reels.",
            TraumaEffects =
            [
                new TraumaEffect(TraumaEffectKind.AdditionalHits, Magnitude: 2),
                new TraumaEffect(
                    TraumaEffectKind.Injured,
                    Magnitude: 1,
                    DurationUnit: TraumaDurationUnit.UntilHealed,
                    Detail: "weapon arm"),
            ],
            Mishap = false,
        };
    }
}

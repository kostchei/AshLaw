using Ash.Core;
using Ash.Rules;

namespace Ash.Sim;

/// <summary>
/// How much of the player an NPC has worked out.
/// </summary>
public enum Awareness : byte
{
    /// <summary>Has not noticed anything.</summary>
    Unaware = 0,

    /// <summary>Something is there; it has not resolved into a threat yet.</summary>
    Recognizing = 1,

    /// <summary>Has just cried out. Committed, but not yet moving.</summary>
    Alerted = 2,

    /// <summary>Fighting.</summary>
    Engaged = 3,
}

/// <summary>What a monster was doing when the player encountered it.</summary>
public enum MonsterActivity : byte
{
    HuntingOrStalking = 0,
    EatingOrFeasting = 1,
    SearchingOrGathering = 2,
    SocializingOrPlaying = 3,
    GuardingOrResting = 4,
    Sleeping = 5,
}

/// <summary>The monster's initial attitude toward the player.</summary>
public enum MonsterReaction : byte
{
    Hostile = 0,
    Suspicious = 1,
    Neutral = 2,
    Curious = 3,
    Friendly = 4,
}

public enum CombatEventKind : byte
{
    /// <summary>An NPC recognised the player and made its noise.</summary>
    Alerted = 0,

    /// <summary>A blow landed.</summary>
    Swing = 1,

    /// <summary>A blow finished off its target.</summary>
    Killed = 2,

    /// <summary>A round of the death clock was rolled.</summary>
    DeathSave = 3,

    /// <summary>The death clock ran out in the body's favour.</summary>
    Stabilised = 4,

    /// <summary>An attack entered its non-zero wind-up.</summary>
    AttackStarted = 5,

    /// <summary>The target left reach before impact.</summary>
    Whiffed = 6,

    /// <summary>The attacker or attack source became invalid before impact.</summary>
    Interrupted = 7,
}

public enum AttackActionPhase : byte
{
    WindUp,
    Resolved,
    Whiffed,
    Interrupted,
}

[Flags]
public enum AttackInterruptionPolicy : byte
{
    None = 0,
    AttackerInvalid = 1 << 0,
    TargetInvalid = 1 << 1,
    SourceChanged = 1 << 2,
    MapChanged = 1 << 3,
    Default = AttackerInvalid | TargetInvalid | SourceChanged | MapChanged,
}

public readonly record struct AttackAction(
    ObjectId AttackerId,
    ObjectId TargetId,
    ObjectId WeaponId,
    long StartTick,
    long ImpactTick,
    long RecoveryTick,
    AttackActionPhase Phase,
    AttackInterruptionPolicy InterruptionPolicy,
    string? Outcome = null);

/// <summary>
/// Something a fight did on one combat beat, for the presentation layer to
/// sound and draw. The simulation never plays audio; it says what was heard.
/// </summary>
public readonly record struct CombatEvent(
    CombatEventKind Kind,
    long Tick,
    ObjectId ActorId,
    ObjectId TargetId,
    CreatureVoice Voice,
    int Damage,
    int RemainingHealth,
    string Message);

/// <summary>What a swing attempt did, and how long until the next one.</summary>
public readonly record struct SwingAttempt(
    bool Swung,
    int CooldownRemainingMilliseconds,
    string Message);

/// <summary>
/// What a step attempt did, and what is left of the round's move.
/// </summary>
public readonly record struct StepAttempt(
    bool Stepped,
    int StepsRemaining,
    string Message);

/// <summary>
/// The pacing of a fight: who has noticed whom, when they cried out, and when
/// anyone may next swing.
/// </summary>
/// <remarks>
/// The whole point of the timings is that the player gets to answer. An NPC
/// spends a second to six working out that the shape in front of it is a
/// problem, shouts, and only then — a beat later — acts; six seconds separate
/// one swing from the next, for the player exactly as much as for the NPC. So
/// the audible cue is a real warning with real time behind it, and a swing is a
/// decision rather than a keypress rate.
///
/// This state is deliberately not saved. Recognition and swing timers describe
/// a fight in progress, and a loaded world starts standing still: NPCs have not
/// noticed you yet and nobody is mid-swing. Persisting a half-elapsed
/// recognition would restore a warning the player never heard.
/// </remarks>
public sealed class CombatDirector
{
    /// <summary>The beat between crying out and acting on it.</summary>
    public const int AlertToActionMilliseconds = 200;

    /// <summary>Recognition takes 1d6 seconds.</summary>
    public const int RecognitionDiceSides = 6;

    /// <summary>How far an NPC can start recognising the player from.</summary>
    public const int AwarenessRadiusTiles = 6;

    /// <summary>
    /// A monster spends its six tiles of round movement evenly across the
    /// round instead of crossing all six on one 200 ms beat.
    /// </summary>
    public const int NpcStepIntervalMilliseconds =
        AttackSpeed.RoundMilliseconds / MovementAllowance.StepsPerRound;

    /// <summary>
    /// The round the death clock is counted in: the same six seconds a swing
    /// takes, so a body bleeding out and a body fighting are on one schedule.
    /// </summary>
    public const int DeathSaveIntervalMilliseconds = 6000;

    private readonly ObjectStore _objects;
    private readonly MovementSolver _movement;
    private readonly ObjectTransferService _transfers;
    private readonly CombatClock _clock;
    private readonly Dice _dice;
    private readonly ActorVitality _vitality;
    private readonly CombatAttackService _attacks;
    private readonly ActorConditionService _conditions;
    private readonly ObjectId _playerId;
    private readonly Dictionary<ObjectId, NpcCombatState> _npcs = [];
    private readonly Dictionary<ObjectId, AttackAction> _activeAttacks = [];
    private readonly Dictionary<ObjectId, AttackAction> _lastAttacks = [];
    private readonly List<CombatEvent> _immediateEvents = [];

    /// <summary>
    /// The beat each of the player's recent steps was taken on. A rolling
    /// window rather than a bucket that refills on a boundary: a bucket would
    /// let a player spend the last of one round and the first of the next
    /// back to back, and cross twice the ground the round allows.
    /// </summary>
    private readonly List<long> _playerSteps = [];

    private long _playerReadyAtTick;

    /// <summary>
    /// The beat the player's next death save falls on. Like recognition and
    /// swing timers this is not saved: a loaded world rolls the next save a
    /// full round after the load rather than resuming a countdown the player
    /// never watched.
    /// </summary>
    private long? _playerDeathSaveAtTick;

    /// <summary>
    /// A fight is wherever the player is. The director takes no map: it reads
    /// the one the player is standing on each beat, so walking into another
    /// subzone leaves that subzone's monsters behind without the world having
    /// to hand combat a new director and lose the round in progress.
    /// </summary>
    public CombatDirector(
        ObjectStore objects,
        MovementSolver movement,
        ObjectTransferService transfers,
        CombatClock clock,
        Dice dice,
        ActorVitality vitality,
        CombatAttackService attacks,
        ActorConditionService conditions,
        ObjectId playerId)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _transfers = transfers ?? throw new ArgumentNullException(nameof(transfers));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dice = dice ?? throw new ArgumentNullException(nameof(dice));
        _vitality = vitality ?? throw new ArgumentNullException(nameof(vitality));
        _attacks = attacks ?? throw new ArgumentNullException(nameof(attacks));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        if (playerId.IsNone)
        {
            throw new ArgumentException(
                "A fight needs a player to be about.",
                nameof(playerId));
        }

        _playerId = playerId;
        _playerReadyAtTick = clock.Tick;
    }

    /// <summary>Combat beats between the cry and the act: one.</summary>
    public static int AlertToActionBeats =>
        CombatClock.BeatsIn(AlertToActionMilliseconds);

    public static int NpcStepBeats =>
        CombatClock.BeatsIn(NpcStepIntervalMilliseconds);

    /// <summary>
    /// Combat beats between one combatant's swings — thirty for the base
    /// one-attack round, fifteen for a fighter swinging a mastery weapon twice.
    /// </summary>
    public int SwingBeatsFor(ObjectId actorId) =>
        CombatClock.BeatsIn(
            AttackSpeed.SwingIntervalMilliseconds(SpeedInputsFor(actorId)));

    /// <summary>
    /// What decides an actor's attack rate. Mastery is a character's list of
    /// mastered weapons compared against what is in hand; no character carries
    /// that list yet, so nothing is mastered and every combatant swings at the
    /// base rate.
    /// </summary>
    private AttackSpeedInputs SpeedInputsFor(ObjectId actorId)
    {
        var actor = _objects.Get(actorId);
        return new AttackSpeedInputs(
            actor.Class,
            actor.Level,
            WieldingMasteryWeapon: false);
    }

    /// <summary>Whether the player's weapon has come back round.</summary>
    public bool PlayerCanSwing => _clock.Tick >= _playerReadyAtTick;

    /// <summary>
    /// How long the player must wait, for the HUD to draw. Zero when ready.
    /// </summary>
    public int PlayerCooldownRemainingMilliseconds =>
        (int)Math.Max(0, _playerReadyAtTick - _clock.Tick) *
        CombatClock.TickMilliseconds;

    /// <summary>
    /// Steps the player has left before the round's move is spent. Refills as
    /// the oldest steps age out of the six-second window rather than all at
    /// once, so a walk settles into a rhythm instead of stuttering.
    /// </summary>
    public int PlayerStepsRemainingInRound
    {
        get
        {
            PruneStepLedger();
            return Math.Max(
                0,
                _conditions.MovementStepsPerRound(
                    _playerId,
                    MovementAllowance.StepsPerRound) - _playerSteps.Count);
        }
    }

    public bool PlayerCanStep => PlayerStepsRemainingInRound > 0;

    public Awareness AwarenessOf(ObjectId npcId) =>
        _npcs.TryGetValue(npcId, out var state) ? state.Awareness : Awareness.Unaware;

    public MonsterActivity? ActivityOf(ObjectId npcId) =>
        _npcs.TryGetValue(npcId, out var state) ? state.Activity : null;

    public MonsterReaction? ReactionOf(ObjectId npcId) =>
        _npcs.TryGetValue(npcId, out var state) ? state.Reaction : null;

    public AttackAction? ActiveAttackOf(ObjectId actorId) =>
        _activeAttacks.TryGetValue(actorId, out var action) ? action : null;

    public AttackAction? LastAttackOf(ObjectId actorId) =>
        _lastAttacks.TryGetValue(actorId, out var action) ? action : null;

    /// <summary>
    /// Advances every NPC one combat beat. The caller runs this only on a beat,
    /// so an NPC's timers are counted in beats and never in frames.
    /// </summary>
    public IReadOnlyList<CombatEvent> Advance()
    {
        var events = new List<CombatEvent>();
        ResolveDueAttacks(events);
        if (!_objects.TryGet(_playerId, out var player) || !player.IsAlive)
        {
            // Nothing to recognise. Existing states stay put rather than being
            // cleared, so a dead player does not reset the room.
            _playerDeathSaveAtTick = null;
            return events;
        }

        // A body that is not upright is not a fight. While the death clock is
        // running it is the only thing running: nobody swings at someone who is
        // already on the floor, so the clock resolves on its own terms instead
        // of racing a stream of blows it can never survive.
        if (!player.Injury.IsUpright)
        {
            AdvanceDeathClock(player, events);
            return events;
        }

        _playerDeathSaveAtTick = null;

        // Object id order, so the same tick sequence always produces the same
        // events in the same order — the dice are shared, and an unordered walk
        // would hand different NPCs different rolls between runs.
        foreach (var npc in LivingNpcs())
        {
            AdvanceNpc(npc, player, events);
        }

        return events;
    }

    /// <summary>
    /// The player's swing, gated by the same six seconds the NPCs wait. The
    /// caller supplies the blow itself; the director owns only its timing.
    /// </summary>
    public SwingAttempt TryPlayerAttack(ObjectId targetId)
    {
        if (!PlayerCanSwing)
        {
            var remaining = PlayerCooldownRemainingMilliseconds;
            return new SwingAttempt(
                false,
                remaining,
                $"Your weapon is still coming back round " +
                $"({remaining / 1000.0:0.0}s).");
        }

        var interval = AttackSpeed.SwingIntervalMilliseconds(
            SpeedInputsFor(_playerId));
        _playerReadyAtTick = checked(
            _clock.Tick + CombatClock.BeatsIn(interval));
        ScheduleAttack(_playerId, targetId, _playerReadyAtTick, _immediateEvents);
        return new SwingAttempt(
            true,
            interval,
            $"You begin your attack; impact in {AlertToActionMilliseconds} ms.");
    }

    public IReadOnlyList<CombatEvent> DrainImmediateEvents()
    {
        var events = _immediateEvents.ToArray();
        _immediateEvents.Clear();
        return events;
    }

    public bool CancelAttack(ObjectId attackerId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!_activeAttacks.Remove(attackerId, out var action))
        {
            return false;
        }

        FinishAction(
            action,
            AttackActionPhase.Interrupted,
            reason,
            _immediateEvents,
            CombatEventKind.Interrupted);
        return true;
    }

    /// <summary>
    /// Schedules a scripted or test-driven intent through the same action path
    /// as player input and NPC decisions.
    /// </summary>
    public AttackAction ScheduleAttackIntent(ObjectId attackerId, ObjectId targetId)
    {
        if (_activeAttacks.ContainsKey(attackerId))
        {
            throw new InvalidOperationException("The attacker is already winding up.");
        }

        var recovery = checked(_clock.Tick + SwingBeatsFor(attackerId));
        ScheduleAttack(attackerId, targetId, recovery, _immediateEvents);
        return _activeAttacks[attackerId];
    }

    /// <summary>A Cleave result grants one immediate follow-up decision.</summary>
    public void GrantPlayerFollowUp() => _playerReadyAtTick = _clock.Tick;

    /// <summary>
    /// Spends one tile of the round's move. Stepping and swinging draw on
    /// separate allowances — the round holds a maximum move and a maximum
    /// number of attacks, and doing one never costs the other.
    /// </summary>
    public StepAttempt TryPlayerStep()
    {
        var remaining = PlayerStepsRemainingInRound;
        if (remaining <= 0)
        {
            return new StepAttempt(
                false,
                0,
                "You have covered as much ground as the round allows.");
        }

        _playerSteps.Add(_clock.Tick);
        return new StepAttempt(true, remaining - 1, "Moved.");
    }

    /// <summary>
    /// Drops steps that have aged out of the round-long window behind the
    /// current beat.
    /// </summary>
    private void PruneStepLedger()
    {
        var oldest = _clock.Tick -
            CombatClock.BeatsIn(AttackSpeed.RoundMilliseconds);
        _playerSteps.RemoveAll(tick => tick <= oldest);
    }

    /// <summary>
    /// Forgets an NPC that has left the world. The director holds handles, and a
    /// destroyed object's slot can be reused, so stale entries must go.
    /// </summary>
    public void Forget(ObjectId npcId)
    {
        _npcs.Remove(npcId);
        _activeAttacks.Remove(npcId);
    }

    /// <summary>
    /// A successful player blow replaces any initial reaction with hostility.
    /// An unobserved monster still takes 1d6 seconds to react; one that has
    /// already resolved the player changes action on the next combat beat.
    /// </summary>
    public void Provoke(ObjectId npcId)
    {
        var npc = _objects.Get(npcId);
        var player = _objects.Get(_playerId);
        if (!_npcs.TryGetValue(npcId, out var state))
        {
            _npcs[npcId] = BeginEncounter(player, provoked: true);
            return;
        }

        var changed = state with
        {
            Reaction = MonsterReaction.Hostile,
            Provoked = true,
        };
        if (state.Awareness is Awareness.Alerted or Awareness.Engaged)
        {
            changed = changed with
            {
                Action = DesiredAction(
                    MonsterReaction.Hostile,
                    TileDistance(npc, player)),
                ActionReadyAtTick = checked(
                    _clock.Tick + AlertToActionBeats),
            };
        }

        _npcs[npcId] = changed;
    }

    private void AdvanceNpc(
        WorldObject npc,
        WorldObject player,
        List<CombatEvent> events)
    {
        var distance = TileDistance(npc, player);
        if (!_npcs.TryGetValue(npc.Id, out var state))
        {
            if (distance <= AwarenessRadiusTiles)
            {
                _npcs[npc.Id] = BeginEncounter(player, provoked: false);
            }

            return;
        }

        switch (state.Awareness)
        {
            case Awareness.Unaware:
                throw new InvalidOperationException(
                    $"{npc.Name} has a tracked but unaware combat state.");

            case Awareness.Recognizing:
                if (distance > AwarenessRadiusTiles && !state.Provoked)
                {
                    // Lost the thread before it resolved. The next approach
                    // rolls again rather than resuming a stale count.
                    _npcs.Remove(npc.Id);
                    return;
                }

                if (_clock.Tick < state.ActionReadyAtTick)
                {
                    return;
                }

                var voice = CreatureVoices.For(npc.TypeId);
                var action = DesiredAction(state.Reaction, distance);
                _npcs[npc.Id] = state with
                {
                    Awareness = Awareness.Alerted,
                    Action = action,
                    ActionReadyAtTick = checked(
                        _clock.Tick + AlertToActionBeats),
                };
                events.Add(
                    new CombatEvent(
                        CombatEventKind.Alerted,
                        _clock.Tick,
                        npc.Id,
                        _playerId,
                        voice,
                        Damage: 0,
                        RemainingHealth: npc.Health,
                        $"{npc.Name}: {CreatureVoices.Utterance(voice)}"));
                return;

            case Awareness.Alerted:
                if (_clock.Tick < state.ActionReadyAtTick)
                {
                    return;
                }

                // The action was chosen on the alert beat and may execute only
                // now, exactly one 200 ms decision beat later.
                state = state with
                {
                    Awareness = Awareness.Engaged,
                };
                _npcs[npc.Id] = state;
                PerformAction(npc, player, state, events);
                return;

            case Awareness.Engaged:
                var desired = DesiredAction(state.Reaction, distance);
                if (desired != state.Action)
                {
                    _npcs[npc.Id] = state with
                    {
                        Action = desired,
                        ActionReadyAtTick = checked(
                            _clock.Tick + AlertToActionBeats),
                    };
                    return;
                }

                if (_clock.Tick < state.ActionReadyAtTick)
                {
                    return;
                }

                PerformAction(npc, player, state, events);
                return;

            default:
                throw new InvalidOperationException(
                    $"{npc.Name} is in an unknown awareness state " +
                    $"{state.Awareness}.");
        }
    }

    private NpcCombatState BeginEncounter(WorldObject player, bool provoked)
    {
        var activity = ActivityFrom(_dice.D6() + _dice.D6());
        var reaction = provoked
            ? MonsterReaction.Hostile
            : ReactionFrom(
                _dice.D6() +
                _dice.D6() +
                RulesRepository.AbilityBonuses.BonusOf(
                    player.Abilities,
                    Ability.Charisma));
        var seconds = _dice.Roll(RecognitionDiceSides);
        return new NpcCombatState(
            Awareness.Recognizing,
            activity,
            reaction,
            NpcAction.Idle,
            Provoked: provoked,
            ActionReadyAtTick: checked(
                _clock.Tick + CombatClock.BeatsIn(seconds * 1000)),
            SwingReadyAtTick: _clock.Tick,
            StepReadyAtTick: _clock.Tick);
    }

    private static MonsterActivity ActivityFrom(int roll) => roll switch
    {
        >= 2 and <= 4 => MonsterActivity.HuntingOrStalking,
        <= 6 => MonsterActivity.EatingOrFeasting,
        <= 8 => MonsterActivity.SearchingOrGathering,
        <= 10 => MonsterActivity.SocializingOrPlaying,
        11 => MonsterActivity.GuardingOrResting,
        12 => MonsterActivity.Sleeping,
        _ => throw new ArgumentOutOfRangeException(nameof(roll)),
    };

    private static MonsterReaction ReactionFrom(int roll) => roll switch
    {
        <= 6 => MonsterReaction.Hostile,
        <= 8 => MonsterReaction.Suspicious,
        9 => MonsterReaction.Neutral,
        <= 11 => MonsterReaction.Curious,
        _ => MonsterReaction.Friendly,
    };

    private static NpcAction DesiredAction(
        MonsterReaction reaction,
        int distance) =>
        reaction != MonsterReaction.Hostile
            ? NpcAction.Idle
            : distance <= 1
                ? NpcAction.Attack
                : NpcAction.Pursue;

    private void ScheduleAttack(
        ObjectId attackerId,
        ObjectId targetId,
        long recoveryTick,
        List<CombatEvent>? events = null)
    {
        if (_activeAttacks.ContainsKey(attackerId))
        {
            return;
        }

        var attacker = _objects.Get(attackerId);
        var action = new AttackAction(
            attackerId,
            targetId,
            _attacks.WeaponOf(attackerId),
            _clock.Tick,
            checked(_clock.Tick + AlertToActionBeats),
            recoveryTick,
            AttackActionPhase.WindUp,
            AttackInterruptionPolicy.Default);
        _activeAttacks.Add(attackerId, action);
        events?.Add(new CombatEvent(
            CombatEventKind.AttackStarted,
            _clock.Tick,
            attackerId,
            targetId,
            CreatureVoices.For(attacker.TypeId),
            0,
            _objects.Get(targetId).Health,
            $"{attacker.Name} begins an attack."));
    }

    private void ResolveDueAttacks(List<CombatEvent> events)
    {
        foreach (var action in _activeAttacks.Values
                     .Where(action => action.ImpactTick <= _clock.Tick)
                     .OrderBy(action => action.AttackerId)
                     .ToArray())
        {
            _activeAttacks.Remove(action.AttackerId);
            ResolveAttack(action, events);
        }
    }

    private void ResolveAttack(AttackAction action, List<CombatEvent> events)
    {
        if (!_objects.TryGet(action.AttackerId, out var attacker) ||
            !attacker.IsAlive || !attacker.Injury.IsUpright ||
            _conditions.PreventsAction(action.AttackerId))
        {
            FinishAction(action, AttackActionPhase.Interrupted,
                "Attack interrupted: the attacker can no longer act.", events,
                CombatEventKind.Interrupted);
            return;
        }

        if (_attacks.WeaponOf(action.AttackerId) != action.WeaponId)
        {
            FinishAction(action, AttackActionPhase.Interrupted,
                $"{attacker.Name}'s attack is interrupted: its weapon changed.", events,
                CombatEventKind.Interrupted);
            return;
        }

        if (!_objects.TryGet(action.TargetId, out var target) || !target.IsAlive)
        {
            FinishAction(action, AttackActionPhase.Interrupted,
                $"{attacker.Name}'s target is no longer there.", events,
                CombatEventKind.Interrupted);
            return;
        }

        if (attacker.Location.Kind != LocationKind.OnMap ||
            target.Location.Kind != LocationKind.OnMap ||
            attacker.Location.MapId != target.Location.MapId)
        {
            FinishAction(action, AttackActionPhase.Interrupted,
                $"{attacker.Name}'s attack is interrupted by separation.", events,
                CombatEventKind.Interrupted);
            return;
        }

        if (TileDistance(attacker, target) > _attacks.MeleeRangeOf(action.AttackerId))
        {
            FinishAction(action, AttackActionPhase.Whiffed,
                $"{attacker.Name} swings where {target.Name} was.", events,
                CombatEventKind.Whiffed);
            return;
        }

        var attack = _attacks.ResolveMelee(action.AttackerId, action.TargetId);
        if (_conditions.Consume(action.AttackerId, TraumaEffectKind.Cleave))
        {
            if (action.AttackerId == _playerId)
            {
                _playerReadyAtTick = _clock.Tick;
            }
            else if (_npcs.TryGetValue(action.AttackerId, out var npcState))
            {
                _npcs[action.AttackerId] = npcState with
                {
                    SwingReadyAtTick = _clock.Tick,
                };
            }
        }
        var completed = action with
        {
            Phase = AttackActionPhase.Resolved,
            Outcome = attack.Hit ? "Impact resolved." : "Attack missed.",
        };
        _lastAttacks[action.AttackerId] = completed;
        events.Add(new CombatEvent(
            CombatEventKind.Swing,
            _clock.Tick,
            action.AttackerId,
            action.TargetId,
            CreatureVoices.For(attacker.TypeId),
            attack.ImmediateHits,
            attack.FinalTargetState.Concussion,
            DescribeResolvedAttack(attacker, target, attack)));
        if (attack.Trauma.CorpseCreated)
        {
            Forget(action.TargetId);
        }
    }

    private void FinishAction(
        AttackAction action,
        AttackActionPhase phase,
        string message,
        List<CombatEvent> events,
        CombatEventKind kind)
    {
        _lastAttacks[action.AttackerId] = action with { Phase = phase, Outcome = message };
        var voice = _objects.TryGet(action.AttackerId, out var attacker)
            && CreatureVoices.All.TryGetValue(attacker.TypeId, out var knownVoice)
            ? knownVoice
            : CreatureVoice.Hail;
        var remaining = _objects.TryGet(action.TargetId, out var target)
            ? target.Health
            : 0;
        events.Add(new CombatEvent(
            kind, _clock.Tick, action.AttackerId, action.TargetId,
            voice, 0, remaining, message));
    }

    private void PerformAction(
        WorldObject npc,
        WorldObject player,
        NpcCombatState state,
        List<CombatEvent> events)
    {
        if (_conditions.PreventsAction(npc.Id))
        {
            return;
        }

        switch (state.Action)
        {
            case NpcAction.Idle:
                return;
            case NpcAction.Pursue:
                Pursue(npc, player, state);
                return;
            case NpcAction.Attack:
                Swing(npc, player, events);
                return;
            default:
                throw new InvalidOperationException(
                    $"{npc.Name} chose an unknown combat action {state.Action}.");
        }
    }

    private void Pursue(
        WorldObject npc,
        WorldObject player,
        NpcCombatState state)
    {
        if (_conditions.PreventsMovement(npc.Id))
        {
            return;
        }

        if (_clock.Tick < state.StepReadyAtTick)
        {
            return;
        }

        // A blocked attempt still consumes this movement interval. Rechecking
        // five times a second would let several monsters jitter against the
        // same occupied spatial cell and would exceed the round's move.
        _npcs[npc.Id] = state with
        {
            StepReadyAtTick = checked(
                _clock.Tick + CombatClock.BeatsIn(
                    AttackSpeed.RoundMilliseconds /
                    _conditions.MovementStepsPerRound(
                        npc.Id,
                        MovementAllowance.StepsPerRound))),
        };

        var from = npc.Location.Position;
        var to = player.Location.Position;
        var deltaX = Math.Sign(to.X - from.X);
        var deltaY = Math.Sign(to.Y - from.Y);
        var xFirst = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
        Span<(int X, int Y)> candidates = stackalloc (int, int)[2];
        candidates[0] = xFirst ? (deltaX, 0) : (0, deltaY);
        candidates[1] = xFirst ? (0, deltaY) : (deltaX, 0);

        foreach (var (x, y) in candidates)
        {
            if (x == 0 && y == 0)
            {
                continue;
            }

            var displacement = new Vec3i(
                x * WorldMap.WorldUnitsPerTile,
                y * WorldMap.WorldUnitsPerTile,
                0);
            var sweep = _movement.Resolve(npc.Id, displacement);
            if (!sweep.ReachedTarget)
            {
                continue;
            }

            var moved = _transfers.Execute(
                [
                    new ObjectTransferRequest(
                        npc.Id,
                        npc.Location,
                        ObjectLocation.OnMap(
                            npc.Location.MapId,
                            sweep.ResolvedPosition)),
                ],
                [sweep.PhysicsFor(npc.Id)]);
            if (!moved.Succeeded)
            {
                throw new InvalidOperationException(
                    $"A resolved pursuit step for {npc.Name} was rejected: " +
                    moved.Message);
            }

            return;
        }
    }

    /// <summary>
    /// One round of the player's death clock, when a round has passed. A body
    /// that came off the clock stable stays where it is: stabilising is not
    /// standing up, and only healing puts a wound back.
    /// </summary>
    private void AdvanceDeathClock(WorldObject player, List<CombatEvent> events)
    {
        if (!player.Injury.IsOnTheDeathClock)
        {
            _playerDeathSaveAtTick = null;
            return;
        }

        var interval = CombatClock.BeatsIn(DeathSaveIntervalMilliseconds);
        if (_playerDeathSaveAtTick is null)
        {
            _playerDeathSaveAtTick = checked(_clock.Tick + interval);
            return;
        }

        if (_clock.Tick < _playerDeathSaveAtTick)
        {
            return;
        }

        var save = _vitality.RollDeathSave(_playerId);
        _playerDeathSaveAtTick = save.Outcome == DeathClockOutcome.Continuing
            ? checked(_clock.Tick + interval)
            : null;
        events.Add(
            new CombatEvent(
                save.Outcome switch
                {
                    DeathClockOutcome.Died => CombatEventKind.Killed,
                    DeathClockOutcome.Stabilised => CombatEventKind.Stabilised,
                    _ => CombatEventKind.DeathSave,
                },
                _clock.Tick,
                _playerId,
                _playerId,
                CreatureVoices.For(player.TypeId),
                0,
                save.State.Concussion,
                DescribeDeathSave(save)));
    }

    private static string DescribeDeathSave(DeathSaveOutcome save) =>
        save.Outcome switch
        {
            DeathClockOutcome.Died => "You stop breathing.",
            DeathClockOutcome.Stabilised =>
                "Your breathing steadies. You are alive, and " +
                Describe(save.NewImpairments) + " will not answer.",
            _ => save.Succeeded
                ? $"You hold on ({save.State.DeathSaveSuccesses} steady, " +
                  $"{save.State.DeathSaveFailures} slipping)."
                : $"You slip further ({save.State.DeathSaveSuccesses} steady, " +
                  $"{save.State.DeathSaveFailures} slipping).",
        };

    /// <summary>Names the abilities a body has lost the reliable use of.</summary>
    public static string Describe(AbilityMask impairments) =>
        impairments == AbilityMask.None
            ? "nothing"
            : string.Join(
                " and ",
                impairments.Abilities().Select(ability => ability.ToString()));

    private void Swing(
        WorldObject npc,
        WorldObject player,
        List<CombatEvent> events)
    {
        var state = _npcs[npc.Id];
        if (_clock.Tick < state.SwingReadyAtTick ||
            TileDistance(npc, player) > 1)
        {
            // Out of reach costs nothing: the swing is not spent, so closing to
            // melee does not also cost a fresh six seconds.
            return;
        }

        _npcs[npc.Id] = state with
        {
            SwingReadyAtTick = checked(
                _clock.Tick + SwingBeatsFor(npc.Id)),
        };
        ScheduleAttack(
            npc.Id,
            _playerId,
            _npcs[npc.Id].SwingReadyAtTick,
            events);
    }

    private string DescribeResolvedAttack(
        WorldObject attacker,
        WorldObject target,
        CombatAttackOutcome attack) =>
        attacker.Id == _playerId
            ? DescribePlayerBlow(target, attack)
            : DescribeBlow(attacker, target, attack);

    private static string DescribePlayerBlow(
        WorldObject target,
        CombatAttackOutcome attack)
    {
        if (!attack.Hit)
        {
            return $"You miss {target.Name} (roll {attack.Request.RawD20}, " +
                $"net {attack.Result.NetRoll}).";
        }

        if (attack.Damage is null)
        {
            return $"Your blow against {target.Name} finds no purchase.";
        }

        if (attack.FinalTargetState.IsDead)
        {
            return $"{target.Name} dies. Its remains can be looted.";
        }

        return $"Hit {target.Name} for {attack.ImmediateHits} damage " +
            $"({attack.FinalTargetState.Concussion}/{target.MaxHealth} HP)." +
            (attack.Result.CriticalTier is { } tier ? $" Critical {tier}." : string.Empty) +
            (string.IsNullOrWhiteSpace(attack.Result.TraumaText)
                ? string.Empty
                : $" {attack.Result.TraumaText}");
    }

    /// <summary>
    /// What a blow reads as. Concussion hits are the ordinary case; a blow that
    /// reaches the wound layer says which ability it took with it, because that
    /// is the cost the player has to notice.
    /// </summary>
    private static string DescribeBlow(
        WorldObject npc,
        WorldObject player,
        CombatAttackOutcome attack)
    {
        if (!attack.Hit)
        {
            return $"{npc.Name} misses you.";
        }

        if (attack.Damage is null)
        {
            return $"{npc.Name}'s blow finds no purchase.";
        }

        var blow = attack.Damage.Value;
        if (attack.FinalTargetState.IsDead)
        {
            return WithTrauma($"{npc.Name} strikes you down.", attack);
        }

        if (blow.EnteredDeathClock)
        {
            return WithTrauma(
                $"{npc.Name} puts you on the floor. You are bleeding out.", attack);
        }

        if (blow.WoundsLost > 0)
        {
            return WithTrauma($"{npc.Name} wounds you ({blow.State.Wounds}/" +
                $"{blow.State.MaximumWounds} wounds). " +
                $"{Describe(blow.NewImpairments)} will not answer.", attack);
        }

        return WithTrauma(
            $"{npc.Name} hits you for {blow.ConcussionLost} " +
            $"({blow.State.Concussion}/{player.MaxHealth} HP).", attack);
    }

    private static string WithTrauma(string message, CombatAttackOutcome attack) =>
        string.IsNullOrWhiteSpace(attack.Result.TraumaText)
            ? message
            : $"{message} {attack.Result.TraumaText}";

    private IReadOnlyList<WorldObject> LivingNpcs() =>
        PlayerMap()
            .QueryAll(ObjectFlags.Monster)
            .Where(candidate => candidate.IsAlive)
            .OrderBy(candidate => candidate.Id)
            .ToArray();

    private WorldMap PlayerMap()
    {
        var player = _objects.Get(_playerId);
        if (player.Location.Kind != LocationKind.OnMap)
        {
            throw new InvalidOperationException(
                $"{player.Name} is not on a map, so there is no room to " +
                "fight in.");
        }

        return _objects.Maps.Get(player.Location.MapId);
    }

    private static int TileDistance(WorldObject from, WorldObject to)
    {
        var a = from.Location.Position;
        var b = to.Location.Position;
        return (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)) /
            WorldMap.WorldUnitsPerTile;
    }

    private enum NpcAction : byte
    {
        Idle = 0,
        Pursue = 1,
        Attack = 2,
    }

    private readonly record struct NpcCombatState(
        Awareness Awareness,
        MonsterActivity Activity,
        MonsterReaction Reaction,
        NpcAction Action,
        bool Provoked,
        long ActionReadyAtTick,
        long SwingReadyAtTick,
        long StepReadyAtTick);
}

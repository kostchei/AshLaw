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

public enum CombatEventKind : byte
{
    /// <summary>An NPC recognised the player and made its noise.</summary>
    Alerted = 0,

    /// <summary>A blow landed.</summary>
    Swing = 1,

    /// <summary>A blow finished off its target.</summary>
    Killed = 2,
}

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

    /// <summary>What an NPC's swing takes off, until weapon rules drive it.</summary>
    public const int NpcSwingDamage = 1;

    private readonly ObjectStore _objects;
    private readonly CombatClock _clock;
    private readonly Dice _dice;
    private readonly ObjectId _playerId;
    private readonly Dictionary<ObjectId, NpcCombatState> _npcs = [];

    /// <summary>
    /// The beat each of the player's recent steps was taken on. A rolling
    /// window rather than a bucket that refills on a boundary: a bucket would
    /// let a player spend the last of one round and the first of the next
    /// back to back, and cross twice the ground the round allows.
    /// </summary>
    private readonly List<long> _playerSteps = [];

    private long _playerReadyAtTick;

    /// <summary>
    /// A fight is wherever the player is. The director takes no map: it reads
    /// the one the player is standing on each beat, so walking into another
    /// subzone leaves that subzone's monsters behind without the world having
    /// to hand combat a new director and lose the round in progress.
    /// </summary>
    public CombatDirector(
        ObjectStore objects,
        CombatClock clock,
        Dice dice,
        ObjectId playerId)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _dice = dice ?? throw new ArgumentNullException(nameof(dice));
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
            return MovementAllowance.StepsPerRound - _playerSteps.Count;
        }
    }

    public bool PlayerCanStep => PlayerStepsRemainingInRound > 0;

    public Awareness AwarenessOf(ObjectId npcId) =>
        _npcs.TryGetValue(npcId, out var state) ? state.Awareness : Awareness.Unaware;

    /// <summary>
    /// Advances every NPC one combat beat. The caller runs this only on a beat,
    /// so an NPC's timers are counted in beats and never in frames.
    /// </summary>
    public IReadOnlyList<CombatEvent> Advance()
    {
        var events = new List<CombatEvent>();
        if (!_objects.TryGet(_playerId, out var player) || !player.IsAlive)
        {
            // Nothing to recognise. Existing states stay put rather than being
            // cleared, so a dead player does not reset the room.
            return events;
        }

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
    public SwingAttempt TryPlayerSwing()
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
        return new SwingAttempt(true, interval, "You swing.");
    }

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
    public void Forget(ObjectId npcId) => _npcs.Remove(npcId);

    private void AdvanceNpc(
        WorldObject npc,
        WorldObject player,
        List<CombatEvent> events)
    {
        var distance = TileDistance(npc, player);
        var state = _npcs.TryGetValue(npc.Id, out var existing)
            ? existing
            : NpcCombatState.Unaware;

        switch (state.Awareness)
        {
            case Awareness.Unaware:
                if (distance > AwarenessRadiusTiles)
                {
                    return;
                }

                // 1d6 seconds. The roll is in seconds because that is the unit
                // the design is written in; the clock converts it to beats.
                var seconds = _dice.Roll(RecognitionDiceSides);
                _npcs[npc.Id] = state with
                {
                    Awareness = Awareness.Recognizing,
                    ReadyAtTick = checked(
                        _clock.Tick + CombatClock.BeatsIn(seconds * 1000)),
                };
                return;

            case Awareness.Recognizing:
                if (distance > AwarenessRadiusTiles)
                {
                    // Lost the thread before it resolved. The next approach
                    // rolls again rather than resuming a stale count.
                    _npcs.Remove(npc.Id);
                    return;
                }

                if (_clock.Tick < state.ReadyAtTick)
                {
                    return;
                }

                var voice = CreatureVoices.For(npc.TypeId);
                _npcs[npc.Id] = state with
                {
                    Awareness = Awareness.Alerted,
                    ReadyAtTick = checked(_clock.Tick + AlertToActionBeats),
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
                if (_clock.Tick < state.ReadyAtTick)
                {
                    return;
                }

                // Engaged and ready: the first swing may land on this beat, so
                // the alert costs exactly the 200 ms it claims to.
                _npcs[npc.Id] = state with
                {
                    Awareness = Awareness.Engaged,
                    ReadyAtTick = _clock.Tick,
                };
                Swing(npc, player, events);
                return;

            case Awareness.Engaged:
                Swing(npc, player, events);
                return;

            default:
                throw new InvalidOperationException(
                    $"{npc.Name} is in an unknown awareness state " +
                    $"{state.Awareness}.");
        }
    }

    private void Swing(
        WorldObject npc,
        WorldObject player,
        List<CombatEvent> events)
    {
        var state = _npcs[npc.Id];
        if (_clock.Tick < state.ReadyAtTick ||
            TileDistance(npc, player) > 1)
        {
            // Out of reach costs nothing: the swing is not spent, so closing to
            // melee does not also cost a fresh six seconds.
            return;
        }

        _npcs[npc.Id] = state with
        {
            ReadyAtTick = checked(_clock.Tick + SwingBeatsFor(npc.Id)),
        };
        var remaining = _objects.Damage(_playerId, NpcSwingDamage);
        events.Add(
            new CombatEvent(
                remaining > 0 ? CombatEventKind.Swing : CombatEventKind.Killed,
                _clock.Tick,
                npc.Id,
                _playerId,
                CreatureVoices.For(npc.TypeId),
                NpcSwingDamage,
                remaining,
                remaining > 0
                    ? $"{npc.Name} hits you for {NpcSwingDamage} " +
                      $"({remaining}/{player.MaxHealth} HP)."
                    : $"{npc.Name} strikes you down."));
    }

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

    private readonly record struct NpcCombatState(
        Awareness Awareness,
        long ReadyAtTick)
    {
        public static NpcCombatState Unaware => new(Awareness.Unaware, 0);
    }
}

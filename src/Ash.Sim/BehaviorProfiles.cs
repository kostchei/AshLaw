namespace Ash.Sim;

/// <summary>
/// How a creature fights, as distinct from how hard it hits.
/// </summary>
/// <remarks>
/// The numbers a monster carries — its attack bonus, its armour, its dice —
/// already say how dangerous it is. None of them say whether it will stand its
/// ground, and that is the thing a player actually reads off a fight. These four
/// are the recognisable answers: one that will not leave its post, one that
/// brings friends and works around your flank, one that will not stay in reach,
/// and one that stops fighting when the fight stops going its way.
/// </remarks>
public enum BehaviorProfile : byte
{
    /// <summary>Holds a post; pursues only as far as the post allows.</summary>
    Guard = 0,

    /// <summary>Calls its kind, pursues far, and works for a flanking tile.</summary>
    PackHunter = 1,

    /// <summary>Strikes and withdraws out of reach before closing again.</summary>
    Skirmisher = 2,

    /// <summary>Breaks off and runs once its body has taken enough.</summary>
    Coward = 3,
}

/// <summary>
/// The tunables one profile decides. Everything here is read by
/// <see cref="CombatDirector"/> on the beat it decides an action; nothing is
/// cached on the creature, so replacing a profile changes behaviour immediately.
/// </summary>
public sealed record BehaviorProfileDefinition(
    BehaviorProfile Profile,
    string Name,

    /// <summary>How far from home the creature will chase before turning back.</summary>
    int PursuitRadiusTiles,

    /// <summary>How far away it can still keep track of a target it has seen.</summary>
    int TrackingRadiusTiles,

    /// <summary>Whether recognising a threat rouses nearby creatures of its kind.</summary>
    bool AlertsKin,

    /// <summary>Whether it prefers a tile no ally is already attacking from.</summary>
    bool SeeksFlank,

    /// <summary>Beats spent backing off after a committed swing. Zero never withdraws.</summary>
    int WithdrawBeats,

    /// <summary>How far it backs off to during a withdrawal.</summary>
    int WithdrawRangeTiles,

    /// <summary>
    /// The fraction of its concussion track at or below which it stops fighting.
    /// Zero never breaks.
    /// </summary>
    double BreakAtHealthFraction)
{
    public bool Withdraws => WithdrawBeats > 0;

    public bool Breaks => BreakAtHealthFraction > 0;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException("A behaviour profile needs a name.", nameof(Name));
        }

        if (PursuitRadiusTiles < 1 || TrackingRadiusTiles < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(PursuitRadiusTiles));
        }

        if (WithdrawBeats < 0 || WithdrawRangeTiles < 0 ||
            (Withdraws && WithdrawRangeTiles < 1))
        {
            throw new ArgumentOutOfRangeException(nameof(WithdrawBeats));
        }

        if (BreakAtHealthFraction is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BreakAtHealthFraction));
        }
    }
}

/// <summary>
/// Which creature fights which way. Immutable content keyed by type id, with a
/// per-actor override for scripts and scenarios (AI-005).
/// </summary>
public sealed class BehaviorProfileCatalog
{
    /// <summary>
    /// A body this small has no second serious hit in it, so running is the
    /// correct read of its own odds rather than a personality trait.
    /// </summary>
    public const int FragileHitPoints = 3;

    public static readonly BehaviorProfileDefinition Guard = new(
        BehaviorProfile.Guard,
        "Guard",
        PursuitRadiusTiles: 8,
        TrackingRadiusTiles: 6,
        AlertsKin: false,
        SeeksFlank: false,
        WithdrawBeats: 0,
        WithdrawRangeTiles: 0,
        BreakAtHealthFraction: 0);

    public static readonly BehaviorProfileDefinition PackHunter = new(
        BehaviorProfile.PackHunter,
        "Pack hunter",
        PursuitRadiusTiles: 16,
        TrackingRadiusTiles: 16,
        AlertsKin: true,
        SeeksFlank: true,
        WithdrawBeats: 0,
        WithdrawRangeTiles: 0,
        BreakAtHealthFraction: 0);

    public static readonly BehaviorProfileDefinition Skirmisher = new(
        BehaviorProfile.Skirmisher,
        "Skirmisher",
        PursuitRadiusTiles: 12,
        TrackingRadiusTiles: 12,
        AlertsKin: false,
        SeeksFlank: false,

        // One round out of reach after each blow. Shorter and the withdrawal is
        // decoration; longer and the creature spends most of a fight walking.
        WithdrawBeats: ConditionTiming.BeatsPerRound,
        WithdrawRangeTiles: 3,
        BreakAtHealthFraction: 0);

    public static readonly BehaviorProfileDefinition Coward = new(
        BehaviorProfile.Coward,
        "Coward",
        PursuitRadiusTiles: 6,
        TrackingRadiusTiles: 6,
        AlertsKin: true,
        SeeksFlank: false,
        WithdrawBeats: 0,
        WithdrawRangeTiles: 0,
        BreakAtHealthFraction: 0.5);

    private static readonly IReadOnlyDictionary<BehaviorProfile, BehaviorProfileDefinition>
        Definitions = new Dictionary<BehaviorProfile, BehaviorProfileDefinition>
        {
            [BehaviorProfile.Guard] = Guard,
            [BehaviorProfile.PackHunter] = PackHunter,
            [BehaviorProfile.Skirmisher] = Skirmisher,
            [BehaviorProfile.Coward] = Coward,
        };

    /// <summary>
    /// Type ids whose behaviour is authored rather than derived. The cave rat is
    /// here because the vertical slice has always had it call its kin, and it
    /// carries no monster profile to derive that from.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, BehaviorProfile> Authored =
        new Dictionary<string, BehaviorProfile>(StringComparer.Ordinal)
        {
            [CombatProfileCatalog.CaveRatTypeId] = BehaviorProfile.PackHunter,
        };

    private readonly Dictionary<ObjectId, BehaviorProfile> _overrides = [];

    public static BehaviorProfileDefinition Definition(BehaviorProfile profile) =>
        Definitions.TryGetValue(profile, out var definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(profile));

    /// <summary>
    /// The profile a creature type fights with, before any per-actor override.
    /// </summary>
    /// <remarks>
    /// Derived from what the monster content already says about itself rather
    /// than from a second authored column: a creature the bestiary calls a pack
    /// hunter is one here too. Only the fragility rule is new, and it is a rule
    /// about the body rather than about the creature's kind.
    /// </remarks>
    public static BehaviorProfile ProfileForType(string typeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        if (Authored.TryGetValue(typeId, out var authored))
        {
            return authored;
        }

        if (!MonsterCatalog.TryGet(typeId, out var monster))
        {
            return BehaviorProfile.Guard;
        }

        return monster.SpecialAbility switch
        {
            MonsterSpecialAbility.PackHunter => BehaviorProfile.PackHunter,
            MonsterSpecialAbility.Skirmisher or MonsterSpecialAbility.Venomous =>
                BehaviorProfile.Skirmisher,
            _ => monster.HitPoints <= FragileHitPoints
                ? BehaviorProfile.Coward
                : BehaviorProfile.Guard,
        };
    }

    public BehaviorProfile ProfileOf(WorldObject npc) =>
        _overrides.TryGetValue(npc.Id, out var replaced)
            ? replaced
            : ProfileForType(npc.TypeId);

    public BehaviorProfileDefinition DefinitionOf(WorldObject npc) =>
        Definition(ProfileOf(npc));

    /// <summary>
    /// Replaces one creature's behaviour, for a script or an authored encounter.
    /// The override is world state and is saved with the world.
    /// </summary>
    public void Override(ObjectId npcId, BehaviorProfile profile)
    {
        if (npcId.IsNone)
        {
            throw new ArgumentException("A behaviour override needs an actor.", nameof(npcId));
        }

        if (!Enum.IsDefined(profile))
        {
            throw new ArgumentOutOfRangeException(nameof(profile));
        }

        _overrides[npcId] = profile;
    }

    public bool ClearOverride(ObjectId npcId) => _overrides.Remove(npcId);

    public IReadOnlyList<(ObjectId ActorId, BehaviorProfile Profile)> Capture() =>
        _overrides.OrderBy(pair => pair.Key)
            .Select(pair => (pair.Key, pair.Value))
            .ToArray();

    public void Restore(IEnumerable<(ObjectId ActorId, BehaviorProfile Profile)> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        _overrides.Clear();
        foreach (var (actorId, profile) in overrides)
        {
            Override(actorId, profile);
        }
    }

    static BehaviorProfileCatalog()
    {
        foreach (var definition in Definitions.Values)
        {
            definition.Validate();
        }
    }
}

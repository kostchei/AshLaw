using Ash.Core;
using Ash.Rules;

namespace Ash.Sim;

public enum SpellCastFailure : byte
{
    None = 0,
    UnknownSpell = 1,
    CasterInvalid = 2,

    /// <summary>A condition the caster is under stops them acting at all.</summary>
    Incapacitated = 3,

    AlreadyCasting = 4,

    /// <summary>The tradition's armour or shield restriction refuses the loadout.</summary>
    Restricted = 5,

    MissingReagents = 6,

    /// <summary>A mishap took this spell away until the next dawn.</summary>
    LockedOut = 7,

    /// <summary>The targeting mode was given the wrong kind of target, or none.</summary>
    IllegalTarget = 8,

    OutOfRange = 9,
}

/// <summary>One cast currently winding up.</summary>
public readonly record struct SpellCast(
    ObjectId CasterId,
    string SpellId,
    ObjectId TargetId,
    Vec3i AimPoint,
    long StartTick,
    long ReleaseTick);

/// <summary>A spell taken away from one caster until a stated beat.</summary>
public readonly record struct SpellLockout(
    ObjectId CasterId,
    string SpellId,
    long ExpiresAtTick);

public sealed record SpellCastAttempt(
    bool Accepted,
    SpellCastFailure Failure,
    string Message,
    SpellCast? Cast = null,

    /// <summary>
    /// Creatures in reach when the cast began. Casting in melee provokes them
    /// (<c>spellcasting_rules.provokes_in_melee</c>); the combat director owns
    /// whether they take the opening.
    /// </summary>
    IReadOnlyList<ObjectId>? Provoked = null)
{
    public static SpellCastAttempt Refused(SpellCastFailure failure, string message) =>
        new(false, failure, message);
}

public enum SpellReleaseOutcome : byte
{
    /// <summary>The cast completed and the effect left the caster's hands.</summary>
    Released = 0,

    /// <summary>The cast completed but the cost could no longer be paid.</summary>
    Fizzled = 1,

    /// <summary>The caster stopped being able to finish.</summary>
    Interrupted = 2,
}

public sealed record SpellRelease(
    SpellCast Cast,
    SpellReleaseOutcome Outcome,
    string Message,
    ProjectileFlight? Flight = null);

/// <summary>
/// Casting: what it costs, how long it takes, what stops it, and what it puts in
/// the air.
/// </summary>
/// <remarks>
/// The rules service already owns the parts of magic that are rules — the armour
/// restrictions per tradition, the natural-1 mishap and what it costs, and the
/// attack tables a bolt and a ball resolve on. This service owns only the parts
/// that are world: whether the reagents are actually in the caster's pack,
/// whether a target is actually there, whether the wind-up survived contact, and
/// what leaves the hand when it does.
///
/// A released spell becomes an ordinary tracked projectile, which is what makes
/// a fireball dodgeable and a bolt saveable. Nothing here resolves damage; the
/// projectile's impact does, through the same one call site every other attack
/// uses.
/// </remarks>
public sealed class SpellCastingService
{
    private readonly ObjectStore _objects;
    private readonly ActorSheets _sheets;
    private readonly ActorConditionService _conditions;
    private readonly CombatClock _clock;
    private readonly ProjectileSystem _projectiles;
    private readonly SpellcastingRules _rules;
    private readonly Dictionary<ObjectId, SpellCast> _casting = [];
    private readonly Dictionary<(ObjectId Caster, string SpellId), long> _lockouts = [];

    public SpellCastingService(
        ObjectStore objects,
        ActorSheets sheets,
        ActorConditionService conditions,
        CombatClock clock,
        ProjectileSystem projectiles,
        SpellcastingRules rules)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _sheets = sheets ?? throw new ArgumentNullException(nameof(sheets));
        _conditions = conditions ?? throw new ArgumentNullException(nameof(conditions));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
    }

    public SpellCast? CastInProgress(ObjectId casterId) =>
        _casting.TryGetValue(casterId, out var cast) ? cast : null;

    public bool IsLockedOut(ObjectId casterId, string spellId) =>
        _lockouts.TryGetValue((casterId, spellId), out var expiry) &&
        _clock.Tick < expiry;

    public IReadOnlyList<SpellLockout> Lockouts => _lockouts
        .OrderBy(pair => pair.Key.Caster)
        .ThenBy(pair => pair.Key.SpellId, StringComparer.Ordinal)
        .Select(pair => new SpellLockout(pair.Key.Caster, pair.Key.SpellId, pair.Value))
        .ToArray();

    /// <summary>
    /// How many of a spell's reagent the caster is carrying. Worn and held items
    /// do not count: a reagent has to be reachable, and what is in your fist is
    /// the weapon.
    /// </summary>
    public int ReagentsCarried(ObjectId casterId, string reagentTypeId)
    {
        if (string.IsNullOrEmpty(reagentTypeId))
        {
            return 0;
        }

        return Reagents(casterId, reagentTypeId).Sum(value => value.Quantity);
    }

    /// <summary>
    /// Checks everything a cast needs and, if it all holds, starts the wind-up.
    /// </summary>
    /// <remarks>
    /// Every refusal is a returned value rather than an exception: an
    /// unaffordable spell is an ordinary thing for a player to try, and the UI
    /// needs to say which of the several reasons it was. Genuinely malformed
    /// input — an unknown spell id, an actor handle that is not an actor — still
    /// throws.
    /// </remarks>
    public SpellCastAttempt Begin(
        ObjectId casterId,
        string spellId,
        ObjectId targetId = default,
        Vec3i? aimPoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spellId);
        if (!SpellCatalog.TryGet(spellId, out var spell))
        {
            return SpellCastAttempt.Refused(
                SpellCastFailure.UnknownSpell, $"There is no spell called '{spellId}'.");
        }

        if (!_objects.TryGet(casterId, out var caster) ||
            !caster.HasFlag(ObjectFlags.Actor) || !caster.IsAlive ||
            !caster.Injury.IsUpright || caster.Location.Kind != LocationKind.OnMap)
        {
            return SpellCastAttempt.Refused(
                SpellCastFailure.CasterInvalid,
                "Only an upright living actor standing on a map can cast.");
        }

        if (_conditions.PreventsAction(casterId))
        {
            return SpellCastAttempt.Refused(
                SpellCastFailure.Incapacitated,
                $"{caster.Name} cannot act.");
        }

        if (_casting.ContainsKey(casterId))
        {
            return SpellCastAttempt.Refused(
                SpellCastFailure.AlreadyCasting,
                $"{caster.Name} is already casting.");
        }

        if (IsLockedOut(casterId, spellId))
        {
            return SpellCastAttempt.Refused(
                SpellCastFailure.LockedOut,
                $"{spell.Name} will not answer again before dawn.");
        }

        var restriction = CanCast(casterId, spell);
        if (!restriction.IsAllowed)
        {
            return SpellCastAttempt.Refused(SpellCastFailure.Restricted, restriction.Reason);
        }

        if (spell.NeedsReagents &&
            ReagentsCarried(casterId, spell.ReagentTypeId) < spell.ReagentQuantity)
        {
            return SpellCastAttempt.Refused(
                SpellCastFailure.MissingReagents,
                $"{spell.Name} needs {spell.ReagentQuantity} of " +
                $"{spell.ReagentTypeId} and you have " +
                $"{ReagentsCarried(casterId, spell.ReagentTypeId)}.");
        }

        var aim = Aim(caster, spell, targetId, aimPoint);
        if (aim.Failure != SpellCastFailure.None)
        {
            return SpellCastAttempt.Refused(aim.Failure, aim.Message);
        }

        var cast = new SpellCast(
            casterId,
            spellId,
            spell.Targeting == SpellTargeting.SingleActor ? targetId : ObjectId.None,
            aim.Point,
            _clock.Tick,
            checked(_clock.Tick + CombatClock.BeatsIn(spell.WindUpMilliseconds)));
        _casting.Add(casterId, cast);
        return new SpellCastAttempt(
            true,
            SpellCastFailure.None,
            $"{caster.Name} begins {spell.Name}.",
            cast,
            _rules.ProvokesInMelee ? Adjacent(caster) : []);
    }

    /// <summary>
    /// Carries every wind-up one beat forward and releases the ones that are
    /// due. Interruption is checked before release, so a caster stunned on the
    /// release beat loses the spell rather than finishing it.
    /// </summary>
    public IReadOnlyList<SpellRelease> Advance()
    {
        var releases = new List<SpellRelease>();
        foreach (var cast in _casting.Values.OrderBy(value => value.CasterId).ToArray())
        {
            if (Interruption(cast) is { } reason)
            {
                _casting.Remove(cast.CasterId);
                releases.Add(new SpellRelease(
                    cast, SpellReleaseOutcome.Interrupted, reason));
                continue;
            }

            if (_clock.Tick < cast.ReleaseTick)
            {
                continue;
            }

            _casting.Remove(cast.CasterId);
            releases.Add(Release(cast));
        }

        return releases;
    }

    public bool Cancel(ObjectId casterId) => _casting.Remove(casterId);

    /// <summary>
    /// Records that a spell went wrong on a natural 1. The rules data decides
    /// the scope and the duration; this only works out when the stated duration
    /// lands on the world clock.
    /// </summary>
    public SpellLockout RecordMishap(ObjectId casterId, string spellId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spellId);
        if (!SpellCatalog.TryGet(spellId, out _))
        {
            throw new InvalidOperationException($"There is no spell called '{spellId}'.");
        }

        if (_rules.MishapLockoutScope != MishapLockoutScope.ThatSpellOnly)
        {
            throw new NotSupportedException(
                $"Mishap lockout scope '{_rules.MishapLockoutScope}' is not implemented.");
        }

        if (_rules.MishapLockoutDuration != MishapLockoutDuration.UntilNextDawn)
        {
            throw new NotSupportedException(
                $"Mishap lockout duration '{_rules.MishapLockoutDuration}' is not implemented.");
        }

        var expiry = WorldCalendar.NextDawnTick(_clock.Tick);
        _lockouts[(casterId, spellId)] = expiry;
        return new SpellLockout(casterId, spellId, expiry);
    }

    /// <summary>Drops lockouts whose dawn has come.</summary>
    public void AdvanceLockouts()
    {
        foreach (var key in _lockouts
                     .Where(pair => pair.Value <= _clock.Tick)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _lockouts.Remove(key);
        }
    }

    public (IReadOnlyList<SpellCast> Casts, IReadOnlyList<SpellLockout> Lockouts) Capture() =>
        (_casting.Values.OrderBy(value => value.CasterId).ToArray(), Lockouts);

    public void Restore(
        IEnumerable<SpellCast> casts,
        IEnumerable<SpellLockout> lockouts)
    {
        ArgumentNullException.ThrowIfNull(casts);
        ArgumentNullException.ThrowIfNull(lockouts);
        _casting.Clear();
        _lockouts.Clear();
        foreach (var cast in casts.OrderBy(value => value.CasterId))
        {
            if (!SpellCatalog.TryGet(cast.SpellId, out _) ||
                cast.StartTick < 0 || cast.ReleaseTick < cast.StartTick ||
                !_objects.TryGet(cast.CasterId, out var caster) ||
                !caster.HasFlag(ObjectFlags.Actor) ||
                !_casting.TryAdd(cast.CasterId, cast))
            {
                throw new ObjectWorldSaveException(
                    $"The saved spell cast for {cast.CasterId} is invalid.");
            }
        }

        foreach (var lockout in lockouts)
        {
            if (!SpellCatalog.TryGet(lockout.SpellId, out _) ||
                lockout.ExpiresAtTick < 0 ||
                !_lockouts.TryAdd((lockout.CasterId, lockout.SpellId), lockout.ExpiresAtTick))
            {
                throw new ObjectWorldSaveException(
                    $"The saved spell lockout for {lockout.CasterId} is invalid.");
            }
        }
    }

    /// <summary>
    /// Whether this caster's tradition permits casting in what they are wearing.
    /// </summary>
    public SpellcastingCheck CanCast(ObjectId casterId, SpellDefinition spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        if (spell.Tradition == CastingTradition.Cleric)
        {
            throw new NotSupportedException(
                "Cleric casting needs the deity virtue scores, which no actor carries yet.");
        }

        var sheet = _sheets.For(casterId);
        return _rules.CanCast(spell.Tradition, sheet.Armor, sheet.Shield);
    }

    private SpellRelease Release(SpellCast cast)
    {
        var spell = SpellCatalog.Get(cast.SpellId);
        var caster = _objects.Get(cast.CasterId);
        if (spell.NeedsReagents && !SpendReagents(cast.CasterId, spell))
        {
            return new SpellRelease(
                cast,
                SpellReleaseOutcome.Fizzled,
                $"{spell.Name} fizzles: the reagents are gone.");
        }

        var aim = cast.AimPoint;
        if (spell.Targeting == SpellTargeting.SingleActor)
        {
            // A bolt is aimed at where the creature is when it leaves the hand,
            // not where it was when the words started. A creature that has left
            // the map entirely takes the aim point with it, so the bolt goes
            // where it was last standing and hits whatever is there now.
            if (_objects.TryGet(cast.TargetId, out var target) &&
                target.Location.Kind == LocationKind.OnMap &&
                target.Location.MapId == caster.Location.MapId &&
                TileDistance(caster.Location.Position, target.Location.Position) <=
                    spell.RangeTiles)
            {
                aim = target.Location.Position;
            }
        }
        else if (spell.Targeting == SpellTargeting.SelfCentered)
        {
            aim = caster.Location.Position;
        }

        var flight = _projectiles.Launch(cast.CasterId, cast.TargetId, spell.Projectile, aim);
        return new SpellRelease(
            cast,
            SpellReleaseOutcome.Released,
            $"{caster.Name} releases {spell.Name}.",
            flight);
    }

    private string? Interruption(SpellCast cast)
    {
        if (!_objects.TryGet(cast.CasterId, out var caster) || !caster.IsAlive ||
            !caster.Injury.IsUpright || caster.Location.Kind != LocationKind.OnMap)
        {
            return "The cast is interrupted: the caster can no longer finish it.";
        }

        // The rules data names the two things that break concentration. A stun
        // is the condition a critical leaves behind, so checking the condition
        // covers both without guessing at a second definition of "was hit hard".
        if (_rules.InterruptionOnCriticalOrStun && _conditions.PreventsAction(cast.CasterId))
        {
            return $"{caster.Name}'s cast is broken.";
        }

        return null;
    }

    private (SpellCastFailure Failure, string Message, Vec3i Point) Aim(
        WorldObject caster,
        SpellDefinition spell,
        ObjectId targetId,
        Vec3i? aimPoint)
    {
        switch (spell.Targeting)
        {
            case SpellTargeting.SelfCentered:
                return targetId.IsNone && aimPoint is null
                    ? (SpellCastFailure.None, "", caster.Location.Position)
                    : (SpellCastFailure.IllegalTarget,
                        $"{spell.Name} is cast on the caster and takes no target.",
                        default);

            case SpellTargeting.SingleActor:
                if (!_objects.TryGet(targetId, out var target) ||
                    !target.HasFlag(ObjectFlags.Actor) || !target.IsAlive ||
                    target.Location.Kind != LocationKind.OnMap ||
                    target.Location.MapId != caster.Location.MapId ||
                    targetId == caster.Id)
                {
                    return (SpellCastFailure.IllegalTarget,
                        $"{spell.Name} needs another living creature on this map.",
                        default);
                }

                return TileDistance(caster.Location.Position, target.Location.Position) >
                        spell.RangeTiles
                    ? (SpellCastFailure.OutOfRange,
                        $"{target.Name} is beyond {spell.Name}'s {spell.RangeTiles} tiles.",
                        default)
                    : (SpellCastFailure.None, "", target.Location.Position);

            case SpellTargeting.MapPoint:
                if (aimPoint is not { } point || !targetId.IsNone)
                {
                    return (SpellCastFailure.IllegalTarget,
                        $"{spell.Name} is aimed at a place, not a creature.",
                        default);
                }

                return TileDistance(caster.Location.Position, point) > spell.RangeTiles
                    ? (SpellCastFailure.OutOfRange,
                        $"That place is beyond {spell.Name}'s {spell.RangeTiles} tiles.",
                        default)
                    : (SpellCastFailure.None, "", point);

            default:
                throw new ArgumentOutOfRangeException(nameof(spell));
        }
    }

    /// <summary>
    /// Living actors close enough to strike the caster. Casting where a blade
    /// can reach you is what provokes.
    /// </summary>
    private IReadOnlyList<ObjectId> Adjacent(WorldObject caster)
    {
        var map = _objects.Maps.Get(caster.Location.MapId);
        var reach = 2 * WorldMap.WorldUnitsPerTile;
        var position = caster.Location.Position;
        return map.Query(
                new WorldRectangle(
                    checked(position.X - reach),
                    checked(position.X + reach),
                    checked(position.Y - reach),
                    checked(position.Y + reach)),
                ObjectFlags.Actor)
            .Where(value => value.Id != caster.Id && value.IsAlive &&
                value.Injury.IsUpright &&
                TileDistance(value.Location.Position, position) <= 1)
            .Select(value => value.Id)
            .Order()
            .ToArray();
    }

    private IReadOnlyList<WorldObject> Reagents(ObjectId casterId, string reagentTypeId) =>
        _objects.Enumerate()
            .Where(value =>
                value.Location.Kind == LocationKind.InContainer &&
                value.Location.Parent == casterId &&
                string.Equals(value.TypeId, reagentTypeId, StringComparison.Ordinal))
            .OrderBy(value => value.Id)
            .ToArray();

    /// <summary>
    /// Burns the spell's reagents in one commit, so a cast never half-pays.
    /// </summary>
    private bool SpendReagents(ObjectId casterId, SpellDefinition spell)
    {
        var owing = spell.ReagentQuantity;
        var quantities = new List<ObjectQuantityUpdate>();
        var destroy = new List<ObjectId>();
        foreach (var stack in Reagents(casterId, spell.ReagentTypeId))
        {
            if (owing <= 0)
            {
                break;
            }

            var spent = Math.Min(owing, stack.Quantity);
            owing -= spent;
            if (spent == stack.Quantity)
            {
                destroy.Add(stack.Id);
            }
            else
            {
                quantities.Add(new ObjectQuantityUpdate(stack.Id, stack.Quantity - spent));
            }
        }

        if (owing > 0)
        {
            return false;
        }

        _objects.CommitStackChange(quantities, destroy, null);
        return true;
    }

    private static int TileDistance(Vec3i a, Vec3i b) =>
        (Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y)) / WorldMap.WorldUnitsPerTile;
}

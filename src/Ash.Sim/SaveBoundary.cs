namespace Ash.Sim;

public enum SaveDeferralReason : byte
{
    None = 0,

    /// <summary>A physics tick is running; its commits are not finished.</summary>
    TickInProgress,

    /// <summary>The request arrived from inside an object-store commit.</summary>
    CommitInProgress,

    /// <summary>
    /// An object is <see cref="LocationKind.InTransfer"/>: a drag or scripted
    /// hand-off owns it and has neither completed nor been cancelled.
    /// </summary>
    TransferInFlight,
}

public readonly record struct SaveAttempt(
    bool Saved,
    bool Deferred,
    SaveDeferralReason Reason,
    long Tick,
    string Message)
{
    public static SaveAttempt Written(long tick, string path) =>
        new(true, false, SaveDeferralReason.None, tick, $"Saved at tick {tick} to {path}.");

    public static SaveAttempt Defer(SaveDeferralReason reason, long tick) =>
        new(
            false,
            true,
            reason,
            tick,
            reason switch
            {
                SaveDeferralReason.TickInProgress =>
                    "Save deferred: a simulation tick is still running.",
                SaveDeferralReason.CommitInProgress =>
                    "Save deferred: an object transaction is still committing.",
                SaveDeferralReason.TransferInFlight =>
                    "Save deferred: an object is still in transfer.",
                _ => "Save deferred.",
            });
}

/// <summary>
/// The save boundary. A save happens only at the end of a completed tick with
/// no transaction committing and nothing in transfer; a request made at any
/// other moment is held and taken at the next safe tick instead of being
/// refused or, worse, taken anyway.
/// </summary>
/// <remarks>
/// The gate never observes a partially updated object store, spatial index or
/// support graph, because it only ever runs between commits and validates the
/// world invariants before writing.
/// </remarks>
public sealed class WorldSaveGate
{
    private readonly ObjectStore _objects;
    private readonly PhysicsSystem _physics;
    private readonly Ash.Rules.Dice _dice;
    private readonly ActorConditionService? _conditions;
    private readonly CombatDirector? _combat;
    private readonly Func<WorldProgressSnapshot?>? _progress;
    private readonly Func<WorldAiSnapshot>? _ai;
    private readonly string _contentFingerprint;

    public WorldSaveGate(
        ObjectStore objects,
        PhysicsSystem physics,
        Ash.Rules.Dice dice,
        string contentFingerprint,
        ActorConditionService? conditions = null,
        CombatDirector? combat = null,
        Func<WorldProgressSnapshot?>? progress = null,
        Func<WorldAiSnapshot>? ai = null)
    {
        _objects = objects ?? throw new ArgumentNullException(nameof(objects));
        _physics = physics ?? throw new ArgumentNullException(nameof(physics));
        _dice = dice ?? throw new ArgumentNullException(nameof(dice));
        _conditions = conditions;
        _combat = combat;
        _progress = progress;
        _ai = ai;
        ArgumentException.ThrowIfNullOrWhiteSpace(contentFingerprint);
        _contentFingerprint = contentFingerprint;
    }

    /// <summary>The path a deferred save is waiting to be written to.</summary>
    public string? PendingPath { get; private set; }

    public bool IsSavePending => PendingPath is not null;

    /// <summary>Why a save cannot be taken right now, if it cannot.</summary>
    public SaveDeferralReason Blocker()
    {
        if (_physics.IsAdvancing)
        {
            return SaveDeferralReason.TickInProgress;
        }

        if (_objects.IsCommitting)
        {
            return SaveDeferralReason.CommitInProgress;
        }

        foreach (var value in _objects.Enumerate())
        {
            if (value.Location.Kind == LocationKind.InTransfer)
            {
                return SaveDeferralReason.TransferInFlight;
            }
        }

        return SaveDeferralReason.None;
    }

    /// <summary>
    /// Saves now if the world is at a safe point, and otherwise records the
    /// request for <see cref="Flush"/> to take at the next safe tick.
    /// </summary>
    public SaveAttempt Request(string path, ushort currentMapId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var blocker = Blocker();
        if (blocker != SaveDeferralReason.None)
        {
            PendingPath = path;
            return SaveAttempt.Defer(blocker, _physics.Tick);
        }

        PendingPath = null;
        return Write(path, currentMapId);
    }

    /// <summary>
    /// Takes a deferred save if one is waiting and the world is now safe. Call
    /// this at the end of every tick. Returns null when nothing was pending.
    /// </summary>
    public SaveAttempt? Flush(ushort currentMapId)
    {
        if (PendingPath is not { } path)
        {
            return null;
        }

        var blocker = Blocker();
        if (blocker != SaveDeferralReason.None)
        {
            return SaveAttempt.Defer(blocker, _physics.Tick);
        }

        PendingPath = null;
        return Write(path, currentMapId);
    }

    /// <summary>Abandons a deferred save, for a world that is being replaced.</summary>
    public void Cancel() => PendingPath = null;

    private SaveAttempt Write(string path, ushort currentMapId)
    {
        // Step 4 of the boundary: the invariants are checked before the
        // snapshot, so a corrupt world is never written to disk as if it were
        // valid. A violation is a bug in the simulation, not a reason to defer.
        _objects.ValidateInvariants();
        _physics.ValidateInvariants();
        foreach (var map in _objects.Maps.All)
        {
            map.ValidateIndex();
        }

        ObjectWorldSave.WriteFile(
            path,
            ObjectWorldSave.Capture(
                _objects,
                _contentFingerprint,
                _physics.Tick,
                currentMapId,
                _dice.State,
                _conditions?.Capture(),
                _combat?.Capture(),
                _progress?.Invoke(),
                _ai?.Invoke()));
        return SaveAttempt.Written(_physics.Tick, path);
    }
}

using Ash.Rules;

namespace Ash.Sim;

/// <summary>The macro-environment a subzone is dressed as.</summary>
/// <remarks>
/// The simulation never holds a colour. A theme travels to the presentation
/// layer as <see cref="SubzonePlan.ThemeTag"/>, and Godot decides what a desert
/// looks like.
/// </remarks>
public enum SubzoneTheme
{
    Desert,
    Forest,
    CastleInterior,
    CityWard,
    SeasideVillage,
    Catacomb,
}

/// <summary>One of the five paced beats a subzone is built from.</summary>
public enum BeatKind
{
    /// <summary>Combat, or a guardian in the way.</summary>
    Encounter,

    /// <summary>Empty by design: tension, lore, and the theme's props.</summary>
    Atmospheric,

    /// <summary>A physics puzzle, a lore object, something to handle.</summary>
    Interactive,

    /// <summary>Hazard terrain or a pit, always with a visible hint.</summary>
    Hazard,

    /// <summary>The payoff: a chest, gold, gems, a finesse weapon.</summary>
    Reward,
}

/// <summary>How a subzone uses the Z axis.</summary>
public enum Verticality
{
    /// <summary>Every beat on one floor level.</summary>
    Flat,

    /// <summary>Each beat rises above the last.</summary>
    Height,

    /// <summary>Each beat drops below the last.</summary>
    Depth,

    /// <summary>Ascents and descents mixed.</summary>
    Both,
}

/// <summary>Who is standing in an interstitial zone, if anyone.</summary>
public enum TraderKind
{
    None,
    Merchant,
    Tinker,
    Craftsman,
    Healer,
}

/// <summary>
/// The player's progression as generation reads it: a level and a class.
/// </summary>
/// <remarks>
/// This is an input to generation, not a second progression system. Nothing
/// here is stored on an actor; it is read off the <see cref="ActorSheet"/> at
/// the moment a plan is made.
/// </remarks>
public readonly record struct CharacterTier(int Level, CharacterClass Class)
{
    public static CharacterTier Of(WorldObject actor)
    {
        if (!actor.HasFlag(ObjectFlags.Actor))
        {
            throw new ArgumentException(
                $"{actor.Name} is not an actor, so it has no tier.",
                nameof(actor));
        }

        return new CharacterTier(actor.Level, actor.Class);
    }

    /// <summary>
    /// What monsters and treasure scale against. Level is the whole of it: a
    /// class changes how a character solves an encounter, not how hard the
    /// encounter should be.
    /// </summary>
    public int Rank => Level < 1
        ? throw new InvalidOperationException(
            $"A character tier needs a level of at least 1, not {Level}.")
        : Level;
}

/// <summary>
/// One beat's place in a subzone: what it is, the cells it occupies, and the
/// floor height those cells sit at.
/// </summary>
public sealed record BeatPlan(
    int Index,
    BeatKind Kind,
    WorldRectangle Cells,
    int FloorZ);

/// <summary>
/// Everything a seed decides about one subzone, before any object exists.
/// </summary>
public sealed record SubzonePlan(
    int Index,
    ushort MapId,
    ulong Seed,
    SubzoneTheme Theme,
    Verticality Verticality,
    int Width,
    int Depth,
    int CorridorY,
    IReadOnlyList<BeatPlan> Beats,
    int MonsterRank,
    int TreasureRank)
{
    /// <summary>The tag the presentation layer reads a palette from.</summary>
    public string ThemeTag =>
        $"theme_{Theme.ToString().ToLowerInvariant()}";
}

/// <summary>
/// The rest point between two subzones, and whoever is trading there.
/// </summary>
public sealed record TransitionPlan(int Index, ulong Seed, TraderKind Trader)
{
    public bool HasTrader => Trader != TraderKind.None;
}

/// <summary>
/// A whole world as a seed decided it: 18 subzones and the 17 rest points
/// between them.
/// </summary>
public sealed record WorldPlan(
    ulong Seed,
    CharacterTier Tier,
    IReadOnlyList<SubzonePlan> Subzones,
    IReadOnlyList<TransitionPlan> Transitions);

/// <summary>
/// Turns a seed into a <see cref="WorldPlan"/>. Planning is the whole of
/// generation's chance; building a plan adds none.
/// </summary>
/// <remarks>
/// Every subzone draws from dice seeded from the world seed and its own index,
/// never from a walk of the world seed. That is what lets subzone 7 generate
/// identically whether or not the first six were ever planned, which in turn is
/// what lets a test pin one subzone without building seventeen others.
/// </remarks>
public static class WorldPlanner
{
    public const int SubzoneCount = 18;

    public const int BeatsPerSubzone = 5;

    /// <summary>Cells along X given to one beat, room and corridor together.</summary>
    public const int BandWidth = 13;

    /// <summary>Cells along X of the room inside a band.</summary>
    public const int RoomWidth = 9;

    /// <summary>Cells of corridor between one room and the next.</summary>
    public const int CorridorLength = BandWidth - RoomWidth;

    /// <summary>Cells the corridor is thick, so a body is never wedged.</summary>
    public const int CorridorThickness = 3;

    public const int MapWidth = BeatsPerSubzone * BandWidth;

    public const int MinimumMapDepth = 21;

    public const int MaximumMapDepth = 29;

    /// <summary>
    /// How far one beat's floor sits from the last where a subzone is vertical.
    /// Two Ultima VIII levels, which a corridor of
    /// <see cref="CorridorLength"/> cells can ramp at one level per cell.
    /// </summary>
    public const int LevelsPerBeatStep = 2;

    public const int BeatStepZ =
        LevelsPerBeatStep * PlayableSliceWorld.UnitsPerLevel;

    /// <summary>
    /// The middle three beats, which the seed permutes. The first beat is
    /// always an <see cref="BeatKind.Encounter"/> and the last always a
    /// <see cref="BeatKind.Reward"/>: that spine is the 5-Room Dungeon's drama,
    /// and shuffling it away would not be variance but mush.
    /// </summary>
    private static readonly BeatKind[] MiddleBeats =
    [
        BeatKind.Atmospheric,
        BeatKind.Interactive,
        BeatKind.Hazard,
    ];

    private static readonly SubzoneTheme[] Themes =
        Enum.GetValues<SubzoneTheme>();

    private static readonly TraderKind[] Traders =
    [
        TraderKind.Merchant,
        TraderKind.Tinker,
        TraderKind.Craftsman,
        TraderKind.Healer,
    ];

    /// <summary>
    /// The whole world for <paramref name="worldSeed"/>: every subzone, and the
    /// rest point between each consecutive pair.
    /// </summary>
    public static WorldPlan Plan(ulong worldSeed, CharacterTier tier)
    {
        var subzones = new SubzonePlan[SubzoneCount];
        for (var index = 0; index < SubzoneCount; index++)
        {
            subzones[index] = PlanSubzone(worldSeed, index, tier);
        }

        var transitions = new TransitionPlan[SubzoneCount - 1];
        for (var index = 0; index < transitions.Length; index++)
        {
            transitions[index] = PlanTransition(worldSeed, index);
        }

        return new WorldPlan(worldSeed, tier, subzones, transitions);
    }

    /// <summary>
    /// One subzone, planned from the world seed and its index alone. Callers
    /// that only want subzone 7 pay for subzone 7.
    /// </summary>
    public static SubzonePlan PlanSubzone(
        ulong worldSeed,
        int index,
        CharacterTier tier)
    {
        if (index < 0 || index >= SubzoneCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"A world has {SubzoneCount} subzones, indexed from 0.");
        }

        var seed = SubzoneSeed(worldSeed, index);
        var dice = new Dice(seed);

        // Roll order is the plan's contract: theme, depth, verticality, beat
        // order, then beat depths. Inserting a roll anywhere above changes
        // every seed's world, so new choices go on the end.
        var theme = Themes[dice.Roll(Themes.Length) - 1];
        var mapDepth = MinimumMapDepth +
            dice.Roll(MaximumMapDepth - MinimumMapDepth + 1) - 1;
        var verticality = RollVerticality(dice);
        var kinds = RollBeatOrder(dice);
        var corridorY = mapDepth / 2;
        var beats = new BeatPlan[BeatsPerSubzone];
        for (var beat = 0; beat < BeatsPerSubzone; beat++)
        {
            var roomDepth = RollRoomDepth(dice, mapDepth);
            beats[beat] = new BeatPlan(
                beat,
                kinds[beat],
                RoomCells(beat, roomDepth, corridorY),
                FloorZFor(verticality, beat));
        }

        // Difficulty is the character's rank plus how far in the subzone sits,
        // and treasure follows difficulty. A late subzone at low level is meant
        // to be dangerous; that is what makes the order matter.
        var difficulty = tier.Rank + (index / 3);
        return new SubzonePlan(
            index,
            checked((ushort)(index + 1)),
            seed,
            theme,
            verticality,
            MapWidth,
            mapDepth,
            corridorY,
            beats,
            difficulty,
            difficulty);
    }

    public static TransitionPlan PlanTransition(ulong worldSeed, int index)
    {
        if (index < 0 || index >= SubzoneCount - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"A world has {SubzoneCount - 1} transitions, indexed from 0.");
        }

        // Transitions derive from a different stream than subzones, so a
        // subzone's roll order can change without moving every trader.
        var seed = TransitionSeed(worldSeed, index);
        var dice = new Dice(seed);
        var trader = dice.Roll(2) == 1
            ? Traders[dice.Roll(Traders.Length) - 1]
            : TraderKind.None;
        return new TransitionPlan(index, seed, trader);
    }

    /// <summary>
    /// Where a subzone is entered: the near edge of its first beat, on the
    /// corridor line.
    /// </summary>
    /// <remarks>
    /// Deliberately not the room's centre. The first beat is the encounter, and
    /// its guardian stands in the middle of it — arriving there puts the
    /// traveller on top of the monster rather than in front of it.
    /// </remarks>
    public static (int X, int Y) EntranceCell(SubzonePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return (plan.Beats[0].Cells.XMin, plan.CorridorY);
    }

    /// <summary>
    /// The cells joining beat <paramref name="beat"/> to the one after it.
    /// </summary>
    public static WorldRectangle CorridorCells(
        SubzonePlan plan,
        int beat)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (beat < 0 || beat >= BeatsPerSubzone - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(beat),
                beat,
                $"A subzone has {BeatsPerSubzone - 1} corridors.");
        }

        var from = plan.Beats[beat].Cells;
        var to = plan.Beats[beat + 1].Cells;
        var half = CorridorThickness / 2;
        return new WorldRectangle(
            from.XMax,
            to.XMin,
            plan.CorridorY - half,
            plan.CorridorY + half + 1);
    }

    /// <summary>
    /// The floor height of each cell along the corridor after
    /// <paramref name="beat"/>, ramping one Ultima VIII level per cell so the
    /// climb never needs a <see cref="VaultCheck"/>.
    /// </summary>
    /// <remarks>
    /// This is the traversability guarantee the specification asks for, and it
    /// is a guarantee rather than a hope because the ramp is computed from the
    /// two floors it joins. A step of more than one level would be a vault, and
    /// a vault that the dice can fail is not a corridor.
    /// </remarks>
    public static IReadOnlyList<int> CorridorRamp(SubzonePlan plan, int beat)
    {
        var corridor = CorridorCells(plan, beat);
        var fromZ = plan.Beats[beat].FloorZ;
        var toZ = plan.Beats[beat + 1].FloorZ;
        var cells = corridor.Width;
        var levels = Math.Abs(toZ - fromZ) / PlayableSliceWorld.UnitsPerLevel;
        if (levels > cells)
        {
            throw new InvalidOperationException(
                $"Subzone {plan.Index} corridor {beat} is {cells} cells long " +
                $"but must climb {levels} levels; no ramp fits, so the beats " +
                "could not be reached on foot.");
        }

        var step = Math.Sign(toZ - fromZ) * PlayableSliceWorld.UnitsPerLevel;
        var ramp = new int[cells];
        for (var cell = 0; cell < cells; cell++)
        {
            // The climb is taken as early as the corridor allows and the rest
            // of it runs level, which keeps the far end flush with the room it
            // opens into.
            var climbed = Math.Min(cell + 1, levels);
            ramp[cell] = checked(fromZ + (climbed * step));
        }

        return ramp;
    }

    private static Verticality RollVerticality(Dice dice)
    {
        if (dice.Roll(2) == 1)
        {
            return Verticality.Flat;
        }

        return dice.Roll(3) switch
        {
            1 => Verticality.Height,
            2 => Verticality.Depth,
            3 => Verticality.Both,
            var rolled => throw new InvalidOperationException(
                $"A 1d3 cannot roll {rolled}."),
        };
    }

    /// <summary>
    /// Encounter first, reward last, and the three middle beats in a seeded
    /// order — a Fisher-Yates shuffle, so all six orders are equally likely.
    /// </summary>
    private static BeatKind[] RollBeatOrder(Dice dice)
    {
        var middle = MiddleBeats.ToArray();
        for (var index = middle.Length - 1; index > 0; index--)
        {
            var swap = dice.Roll(index + 1) - 1;
            (middle[index], middle[swap]) = (middle[swap], middle[index]);
        }

        return
        [
            BeatKind.Encounter,
            middle[0],
            middle[1],
            middle[2],
            BeatKind.Reward,
        ];
    }

    private static int RollRoomDepth(Dice dice, int mapDepth)
    {
        // A room is always thick enough to hold the corridor mouth plus a cell
        // of floor either side of it, and always leaves a cell of margin at
        // each map edge.
        var minimum = CorridorThickness + 2;
        var maximum = mapDepth - 2;
        return minimum + dice.Roll(maximum - minimum + 1) - 1;
    }

    private static WorldRectangle RoomCells(
        int beat,
        int roomDepth,
        int corridorY)
    {
        var xMin = (beat * BandWidth) + 1;
        var half = roomDepth / 2;
        return new WorldRectangle(
            xMin,
            xMin + RoomWidth,
            corridorY - half,
            corridorY - half + roomDepth);
    }

    private static int FloorZFor(Verticality verticality, int beat) =>
        verticality switch
        {
            Verticality.Flat => 0,
            Verticality.Height => beat * BeatStepZ,
            Verticality.Depth => -beat * BeatStepZ,

            // Up, down, up, down: a mix that still never asks a corridor for
            // more levels than it has cells.
            Verticality.Both => (beat % 2 == 0 ? 1 : -1) * BeatStepZ,
            _ => throw new ArgumentOutOfRangeException(nameof(verticality)),
        };

    /// <summary>
    /// SplitMix64 over the world seed and a stream-tagged index. Adjacent
    /// indices must not give adjacent-looking worlds, and a xorshift state
    /// seeded with a small integer takes a while to look random, so the index
    /// is mixed rather than added.
    /// </summary>
    private static ulong SubzoneSeed(ulong worldSeed, int index) =>
        Mix(worldSeed + (0x9E3779B97F4A7C15UL * (ulong)(index + 1)));

    private static ulong TransitionSeed(ulong worldSeed, int index) =>
        Mix(worldSeed + 0xD1B54A32D192ED03UL +
            (0x9E3779B97F4A7C15UL * (ulong)(index + 1)));

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}

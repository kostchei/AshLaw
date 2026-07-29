namespace Ash.Core;

/// <summary>
/// The axis-aligned volume a visible object occupies, in world units
/// (ADR 0001: 1 tile = 256 units, 1 vertical level = 8 units).
/// </summary>
public readonly record struct SortVolume(
    int XMin,
    int XMax,
    int YMin,
    int YMax,
    int ZMin,
    int ZMax)
{
    public bool IsDegenerate => XMax < XMin || YMax < YMin || ZMax < ZMin;

    /// <summary>A zero-height volume: floors, and other flat items.</summary>
    public bool IsFlat => ZMax == ZMin;

    public void Validate(string parameterName)
    {
        if (IsDegenerate)
        {
            throw new ArgumentException(
                $"Volume has an inverted extent: {this}.",
                parameterName);
        }
    }
}

/// <summary>One entry in the visible set handed to <see cref="VolumeSorter"/>.</summary>
/// <param name="Id">
/// Stable object identity. Used as the final tie-break, so sort output is a
/// function of the input alone.
/// </param>
/// <param name="Volume">The object's footprint and vertical interval.</param>
/// <param name="IsSprite">
/// A camera-facing billboard. Sprites draw after non-sprites they overlap,
/// because their volume does not describe what the player actually sees.
/// </param>
/// <param name="SortBias">
/// Per-shape authored nudge (GFX-055). Higher draws later, i.e. in front.
/// Applied before geometry, so content can override a bad automatic result.
/// </param>
public sealed record SortItem(
    int Id,
    SortVolume Volume,
    bool IsSprite = false,
    int SortBias = 0);

/// <summary>
/// A cycle in the draw-order graph, reported so the map tool can surface it
/// (GFX-054). A cycle is a content problem; it is broken deterministically, never
/// silently.
/// </summary>
public sealed class SortCycleDiagnostic
{
    /// <summary>How many unresolved object ids a diagnostic carries as a sample.</summary>
    public const int MaxSampleSize = 16;

    internal SortCycleDiagnostic(
        int brokenAtObjectId,
        int unresolvedCount,
        IReadOnlyList<int> sampleObjectIds)
    {
        BrokenAtObjectId = brokenAtObjectId;
        UnresolvedCount = unresolvedCount;
        SampleObjectIds = sampleObjectIds;
    }

    /// <summary>The object promoted to break the cycle.</summary>
    public int BrokenAtObjectId { get; }

    /// <summary>How many objects were still unresolved when the break happened.</summary>
    public int UnresolvedCount { get; }

    /// <summary>
    /// Up to <see cref="MaxSampleSize"/> ids from the unresolved set, to give the
    /// map tool somewhere to start looking.
    /// </summary>
    /// <remarks>
    /// This is a sample of what remains, not the exact strongly-connected
    /// component. Isolating the true cycle members needs an SCC pass; that is
    /// deferred until the map tool (GFX-054) actually consumes this, because
    /// computing it per break would reintroduce the quadratic cost this class was
    /// changed to avoid.
    /// </remarks>
    public IReadOnlyList<int> SampleObjectIds { get; }

    public override string ToString() =>
        $"Draw-order cycle: {UnresolvedCount} objects unresolved, e.g. " +
        $"[{string.Join(", ", SampleObjectIds)}]; " +
        $"broken deterministically at object {BrokenAtObjectId}.";
}

public sealed class SortResult
{
    internal SortResult(
        IReadOnlyList<int> drawOrder,
        IReadOnlyList<SortCycleDiagnostic> cycles,
        int comparisons)
    {
        DrawOrder = drawOrder;
        Cycles = cycles;
        Comparisons = comparisons;
    }

    /// <summary>Object ids in painter order: first drawn is furthest back.</summary>
    public IReadOnlyList<int> DrawOrder { get; }

    public IReadOnlyList<SortCycleDiagnostic> Cycles { get; }

    /// <summary>
    /// Pairwise comparisons actually performed. The point of bucketing is to keep
    /// this far below n²; a perf test asserts against it.
    /// </summary>
    public int Comparisons { get; }
}

namespace Ash.Core;

/// <summary>
/// Produces painter draw order for a visible set by building a directed graph of
/// pairwise "behind" relations and topologically sorting it (ADR 0002).
/// </summary>
/// <remarks>
/// <para>
/// The pairwise predicate is <strong>not a strict weak ordering</strong>: three
/// objects can form a cycle A-behind-B-behind-C-behind-A. Feeding such a
/// predicate to <see cref="List{T}.Sort"/> is undefined behaviour and is the
/// direct cause of the frame-to-frame flicker GFX-052 forbids. Hence a graph and
/// an explicit topological sort rather than a comparison sort.
/// </para>
/// <para>
/// <strong>Status: first cut.</strong> <see cref="Below"/> implements the
/// geometric core — separation on Z, then Y, then X, with flat items and sprites
/// handled explicitly. ADR 0002 names ScummVM's
/// <c>ultima8/world/sort_item.h</c> <c>SortItem::below</c> as the behavioural
/// reference, and the accumulated special cases there (fixed vs dynamic items,
/// the "occludes" fast path, and several shape-flag exceptions) have not been
/// ported line by line. Do that before M2 authors real map content. The graph
/// construction, cycle handling, determinism, and cost characteristics below are
/// independent of that work and are what this spike exists to prove.
/// </para>
/// </remarks>
public static class VolumeSorter
{
    /// <summary>
    /// Screen-space bucket edge, in logical pixels. Two items are only compared if
    /// they share a bucket, which is what keeps the cost off O(n²). Sized to be a
    /// little larger than a typical sprite so most items land in one or two cells.
    /// </summary>
    public const int BucketSize = 32;

    /// <summary>
    /// Whether <paramref name="a"/> must be drawn before (behind)
    /// <paramref name="b"/>.
    /// </summary>
    /// <remarks>
    /// This is a pure ordering predicate over two volumes and says nothing about
    /// whether the two are near each other. Deciding which pairs are worth asking
    /// about is <see cref="Sort"/>'s job: it only builds an edge between items
    /// whose projected screen rectangles actually overlap, since objects that
    /// never touch the same pixel need no relative order.
    /// </remarks>
    public static bool Below(SortItem a, SortItem b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.Id == b.Id)
        {
            return false;
        }

        // Authored bias wins over geometry, so content can correct a bad result.
        if (a.SortBias != b.SortBias)
        {
            return a.SortBias < b.SortBias;
        }

        // Clear vertical separation dominates: lower draws first.
        if (a.Volume.ZMax <= b.Volume.ZMin)
        {
            return true;
        }

        if (b.Volume.ZMax <= a.Volume.ZMin)
        {
            return false;
        }

        // A flat item at the same level as a solid one is its floor, so it draws
        // first regardless of footprint.
        if (a.Volume.IsFlat != b.Volume.IsFlat)
        {
            return a.Volume.IsFlat;
        }

        // Further from the camera draws first. In this projection, greater Y and
        // greater X are nearer.
        if (a.Volume.YMax <= b.Volume.YMin)
        {
            return true;
        }

        if (b.Volume.YMax <= a.Volume.YMin)
        {
            return false;
        }

        if (a.Volume.XMax <= b.Volume.XMin)
        {
            return true;
        }

        if (b.Volume.XMax <= a.Volume.XMin)
        {
            return false;
        }

        // Interpenetrating volumes. A sprite does not really occupy its box, so it
        // draws after whatever it overlaps.
        if (a.IsSprite != b.IsSprite)
        {
            return !a.IsSprite;
        }

        return false;
    }

    public static SortResult Sort(IReadOnlyList<SortItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            item.Volume.Validate(nameof(items));
        }

        var ids = new HashSet<int>();
        foreach (var item in items)
        {
            if (!ids.Add(item.Id))
            {
                throw new ArgumentException(
                    $"Duplicate object id {item.Id} in the visible set.",
                    nameof(items));
            }
        }

        // Process in a deterministic order so the emitted graph, and therefore the
        // draw order, is a function of the input set and not of its enumeration
        // order.
        var ordered = items.OrderBy(TieBreakKey, TieBreakComparer.Instance).ToArray();
        var indexOfId = new Dictionary<int, int>(ordered.Length);
        for (var index = 0; index < ordered.Length; index++)
        {
            indexOfId[ordered[index].Id] = index;
        }

        var successors = new List<int>[ordered.Length];
        var inDegree = new int[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            successors[index] = [];
        }

        var rects = new ScreenRect[ordered.Length];
        for (var index = 0; index < ordered.Length; index++)
        {
            rects[index] = ProjectToScreen(ordered[index].Volume);
        }

        var comparisons = 0;
        foreach (var (left, right) in CandidatePairs(ordered, rects))
        {
            comparisons++;
            var a = ordered[left];
            var b = ordered[right];

            if (Below(a, b))
            {
                successors[left].Add(right);
                inDegree[right]++;
            }
            else if (Below(b, a))
            {
                successors[right].Add(left);
                inDegree[left]++;
            }
        }

        var drawOrder = new List<int>(ordered.Length);
        var cycles = new List<SortCycleDiagnostic>();

        // Kahn's algorithm. Both sets are kept ordered by the same stable key, so
        // ties -- and cycle breaks -- resolve identically every run.
        var comparer = ByTieBreak.Instance;
        var ready = new SortedSet<int>(comparer);

        // Every not-yet-emitted node, so breaking a cycle is a O(log n) lookup of
        // the lowest-keyed survivor rather than an O(n log n) rescan. With a dense
        // scene producing hundreds of breaks, the difference is the whole frame
        // budget.
        var unemitted = new SortedSet<int>(comparer);

        for (var index = 0; index < ordered.Length; index++)
        {
            unemitted.Add(index);
            if (inDegree[index] == 0)
            {
                ready.Add(index);
            }
        }

        // O(1) membership, checked once per graph edge. Doing this through the
        // SortedSet instead cost a comparator call per edge, which at hundreds of
        // thousands of edges dominated the sort.
        var emitted = new bool[ordered.Length];

        while (unemitted.Count > 0)
        {
            if (ready.Count == 0)
            {
                // Everything left is in at least one cycle. Break on the same
                // stable key and report it; never break silently.
                var promoted = unemitted.Min;
                var sample = unemitted
                    .Take(SortCycleDiagnostic.MaxSampleSize)
                    .Select(index => ordered[index].Id)
                    .ToArray();
                cycles.Add(new SortCycleDiagnostic(
                    ordered[promoted].Id,
                    unemitted.Count,
                    sample));

                inDegree[promoted] = 0;
                ready.Add(promoted);
            }

            var next = ready.Min;
            ready.Remove(next);
            if (emitted[next])
            {
                continue;
            }

            emitted[next] = true;
            unemitted.Remove(next);
            drawOrder.Add(ordered[next].Id);

            foreach (var successor in successors[next])
            {
                if (!emitted[successor] && --inDegree[successor] == 0)
                {
                    ready.Add(successor);
                }
            }
        }

        return new SortResult(drawOrder, cycles, comparisons);
    }

    /// <summary>
    /// The axis-aligned screen rectangle a volume projects to. Two objects need a
    /// relative order only if these overlap.
    /// </summary>
    public readonly record struct ScreenRect(int Left, int Right, int Top, int Bottom)
    {
        public bool Overlaps(ScreenRect other) =>
            Left < other.Right && other.Left < Right &&
            Top < other.Bottom && other.Top < Bottom;
    }

    public static ScreenRect ProjectToScreen(SortVolume volume)
    {
        // screen_x = (x - y) / h, so it is widest at (XMin, YMax) and (XMax, YMin).
        // screen_y = (x + y) / 2h - z, so it is highest at (XMin, YMin, ZMax).
        const int h = ObliqueProjection.UnitsPerHorizontalPixel;
        const int v = ObliqueProjection.UnitsPerVerticalPixel;

        var left = FloorDiv(volume.XMin - volume.YMax, h);
        var right = FloorDiv(volume.XMax - volume.YMin, h) + 1;
        var top = FloorDiv(volume.XMin + volume.YMin, h * 2) - FloorDiv(volume.ZMax, v);
        var bottom = FloorDiv(volume.XMax + volume.YMax, h * 2) - FloorDiv(volume.ZMin, v) + 1;

        return new ScreenRect(left, right, top, bottom);
    }

    /// <summary>
    /// Yields only pairs worth comparing. Items are bucketed on a screen-space
    /// grid (§2.5) and a pair is yielded only if its projected rectangles actually
    /// overlap, so the graph carries an edge exactly where painter order matters.
    /// Each unordered pair is yielded at most once.
    /// </summary>
    private static List<(int Left, int Right)> CandidatePairs(
        SortItem[] ordered,
        ScreenRect[] rects)
    {
        var buckets = new Dictionary<(int X, int Y), List<int>>();
        for (var index = 0; index < ordered.Length; index++)
        {
            var rect = rects[index];
            for (var x = FloorDiv(rect.Left, BucketSize); x <= FloorDiv(rect.Right - 1, BucketSize); x++)
            {
                for (var y = FloorDiv(rect.Top, BucketSize); y <= FloorDiv(rect.Bottom - 1, BucketSize); y++)
                {
                    var cell = (x, y);
                    if (!buckets.TryGetValue(cell, out var members))
                    {
                        members = [];
                        buckets[cell] = members;
                    }

                    members.Add(index);
                }
            }
        }

        // Comparing only within a bucket (not with neighbours) is sufficient: two
        // overlapping rectangles necessarily share at least one grid cell.
        //
        // A pair can share several cells, so each pair is assigned to exactly one
        // owner: the cell containing the top-left corner of the two rectangles'
        // intersection. That is an O(1) arithmetic test, which replaces a
        // HashSet of tuples that was costing more than the comparisons it guarded.
        var pairs = new List<(int, int)>();
        foreach (var (cell, members) in buckets)
        {
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var left = members[i];
                    var right = members[j];
                    ref var a = ref rects[left];
                    ref var b = ref rects[right];

                    if (!a.Overlaps(b))
                    {
                        continue;
                    }

                    var overlapLeft = Math.Max(a.Left, b.Left);
                    var overlapTop = Math.Max(a.Top, b.Top);
                    if (FloorDiv(overlapLeft, BucketSize) != cell.X ||
                        FloorDiv(overlapTop, BucketSize) != cell.Y)
                    {
                        continue;
                    }

                    pairs.Add(left < right ? (left, right) : (right, left));
                }
            }
        }

        return pairs;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor != 0 && ((value < 0) != (divisor < 0)) ? quotient - 1 : quotient;
    }

    /// <summary>The stable key from ADR 0002: <c>(x_min + y_min, z_min, ObjectId)</c>.</summary>
    private static (int Depth, int ZMin, int Id) TieBreakKey(SortItem item) =>
        (item.Volume.XMin + item.Volume.YMin, item.Volume.ZMin, item.Id);

    private sealed class TieBreakComparer : IComparer<(int Depth, int ZMin, int Id)>
    {
        public static TieBreakComparer Instance { get; } = new();

        public int Compare((int Depth, int ZMin, int Id) left, (int Depth, int ZMin, int Id) right)
        {
            var depth = left.Depth.CompareTo(right.Depth);
            if (depth != 0)
            {
                return depth;
            }

            var z = left.ZMin.CompareTo(right.ZMin);
            return z != 0 ? z : left.Id.CompareTo(right.Id);
        }
    }

    /// <summary>
    /// Orders node indices by the ADR 0002 stable key. Because <c>ordered</c> is
    /// already sorted on that key, the index order <em>is</em> the key order, so
    /// this reduces to an integer compare — no tuple rebuild per comparison.
    /// </summary>
    private sealed class ByTieBreak : IComparer<int>
    {
        public static ByTieBreak Instance { get; } = new();

        public int Compare(int left, int right) => left.CompareTo(right);
    }
}

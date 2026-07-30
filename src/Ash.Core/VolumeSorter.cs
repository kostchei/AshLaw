// Depth-order and occlusion portions derived from Pentagram ItemSorter.cpp,
// Copyright (C) 2003-2004 The Pentagram team, GPL-2.0-or-later.
// AshLaw is distributed under GPL-2.0-only; see LICENSE and ADR 0005.

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
/// <see cref="Below"/> and the occlusion test are C# ports of Pentagram's
/// GPLv2-compatible <c>world/ItemSorter.cpp</c> at official SourceForge SVN
/// revision 2560. See ADR 0005. The current ScummVM descendant is GPLv3-or-later,
/// so it is a behavioural reference rather than the copied source for this
/// GPLv2-only repository.
/// </para>
/// <para>
/// The graph construction, cycle handling, determinism and cost characteristics
/// below are independent of the predicate and are what this spike proved; they
/// survive the port.
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

        // Crusader-style always-on-top sprites are retained as an explicit
        // extension. Ultima VIII content normally leaves this flag unset.
        if (a.IsSprite != b.IsSprite)
        {
            return !a.IsSprite;
        }

        var av = a.Volume;
        var bv = b.Volume;

        // Ported from Pentagram world/ItemSorter.cpp, SVN r2560,
        // SortItem::operator< (GPL-2.0-or-later), adapted from its x/xleft,
        // y/yfar, z/ztop names to SortVolume min/max extents.
        if (av.IsFlat && bv.IsFlat)
        {
            if (av.ZMax != bv.ZMax)
            {
                return av.ZMax < bv.ZMax;
            }

            if (a.HasFlag(SortItemFlags.Animated) != b.HasFlag(SortItemFlags.Animated))
            {
                return !a.HasFlag(SortItemFlags.Animated);
            }

            if (a.HasFlag(SortItemFlags.Translucent) != b.HasFlag(SortItemFlags.Translucent))
            {
                return !a.HasFlag(SortItemFlags.Translucent);
            }

            if (a.HasFlag(SortItemFlags.Draw) != b.HasFlag(SortItemFlags.Draw))
            {
                return a.HasFlag(SortItemFlags.Draw);
            }

            if (a.HasFlag(SortItemFlags.Solid) != b.HasFlag(SortItemFlags.Solid))
            {
                return a.HasFlag(SortItemFlags.Solid);
            }

            if (a.HasFlag(SortItemFlags.Occludes) != b.HasFlag(SortItemFlags.Occludes))
            {
                return a.HasFlag(SortItemFlags.Occludes);
            }

            if (a.HasFlag(SortItemFlags.LargeFlatSquare) !=
                b.HasFlag(SortItemFlags.LargeFlatSquare))
            {
                return a.HasFlag(SortItemFlags.LargeFlatSquare);
            }
        }
        else
        {
            if (av.ZMax <= bv.ZMin)
            {
                return true;
            }

            if (av.ZMin >= bv.ZMax)
            {
                return false;
            }
        }

        if (av.XMax <= bv.XMin)
        {
            return true;
        }

        if (av.XMin >= bv.XMax)
        {
            return false;
        }

        if (av.YMax <= bv.YMin)
        {
            return true;
        }

        if (av.YMin >= bv.YMax)
        {
            return false;
        }

        if (av.ZMin != bv.ZMin)
        {
            return av.ZMin < bv.ZMin;
        }

        if ((av.ZMax + av.ZMin) / 2 <= bv.ZMin)
        {
            return true;
        }

        if (av.ZMin >= (bv.ZMax + bv.ZMin) / 2)
        {
            return false;
        }

        if ((av.XMax + av.XMin) / 2 <= bv.XMin)
        {
            return true;
        }

        if (av.XMin >= (bv.XMax + bv.XMin) / 2)
        {
            return false;
        }

        if ((av.YMax + av.YMin) / 2 <= bv.YMin)
        {
            return true;
        }

        if (av.YMin >= (bv.YMax + bv.YMin) / 2)
        {
            return false;
        }

        if (av.XMax + av.YMax != bv.XMax + bv.YMax)
        {
            return av.XMax + av.YMax < bv.XMax + bv.YMax;
        }

        if (av.XMin + av.YMin != bv.XMin + bv.YMin)
        {
            return av.XMin + av.YMin < bv.XMin + bv.YMin;
        }

        if (av.XMax != bv.XMax)
        {
            return av.XMax < bv.XMax;
        }

        if (av.YMax != bv.YMax)
        {
            return av.YMax < bv.YMax;
        }

        if (a.ShapeNumber != b.ShapeNumber)
        {
            return a.ShapeNumber < b.ShapeNumber;
        }

        return a.FrameNumber < b.FrameNumber;
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

        var pairs = CandidatePairs(ordered, rects);
        var relations = new List<(int Behind, int Front)>(pairs.Count);
        var occluded = new bool[ordered.Length];
        foreach (var (left, right) in pairs)
        {
            var a = ordered[left];
            var b = ordered[right];

            if (Below(a, b))
            {
                relations.Add((left, right));
                if (b.HasFlag(SortItemFlags.Occludes) &&
                    Occludes(b.Volume, a.Volume))
                {
                    occluded[left] = true;
                }
            }
            else if (Below(b, a))
            {
                relations.Add((right, left));
                if (a.HasFlag(SortItemFlags.Occludes) &&
                    Occludes(a.Volume, b.Volume))
                {
                    occluded[right] = true;
                }
            }
        }

        foreach (var (behind, front) in relations)
        {
            if (occluded[behind] || occluded[front])
            {
                continue;
            }

            successors[behind].Add(front);
            inDegree[front]++;
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
            if (occluded[index])
            {
                continue;
            }

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

        var occludedObjectIds = ordered
            .Where((_, index) => occluded[index])
            .Select(item => item.Id)
            .ToArray();
        return new SortResult(drawOrder, occludedObjectIds, cycles, pairs.Count);
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

    private static bool Occludes(SortVolume front, SortVolume behind)
    {
        // Ported from Pentagram world/ItemSorter.cpp, SVN r2560,
        // SortItem::occludes (GPL-2.0-or-later). Its six half-plane checks
        // determine whether the projected prism in front completely contains
        // the one behind.
        var a = ProjectPrism(front);
        var b = ProjectPrism(behind);

        var topX = a.TopX - b.TopX;
        var topY = a.TopY - b.TopY;
        var bottomX = a.BottomX - b.BottomX;
        var bottomY = a.BottomY - b.BottomY;

        var topLeft = topX + (topY * 2);
        var topRight = -topX + (topY * 2);
        var bottomLeft = bottomX - (bottomY * 2);
        var bottomRight = -bottomX - (bottomY * 2);

        return
            a.RightX >= b.RightX &&
            a.LeftX <= b.LeftX &&
            topLeft <= 0 &&
            topRight <= 0 &&
            bottomLeft <= 0 &&
            bottomRight <= 0;
    }

    private static ProjectedPrism ProjectPrism(SortVolume volume)
    {
        var left = ObliqueProjection.WorldToScreen(
            new Vec3i(volume.XMin, volume.YMax, volume.ZMin));
        var right = ObliqueProjection.WorldToScreen(
            new Vec3i(volume.XMax, volume.YMin, volume.ZMin));
        var top = ObliqueProjection.WorldToScreen(
            new Vec3i(volume.XMin, volume.YMin, volume.ZMax));
        var bottom = ObliqueProjection.WorldToScreen(
            new Vec3i(volume.XMax, volume.YMax, volume.ZMin));

        return new ProjectedPrism(
            left.X,
            right.X,
            top.X,
            top.Y,
            bottom.X,
            bottom.Y);
    }

    private readonly record struct ProjectedPrism(
        int LeftX,
        int RightX,
        int TopX,
        int TopY,
        int BottomX,
        int BottomY);

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

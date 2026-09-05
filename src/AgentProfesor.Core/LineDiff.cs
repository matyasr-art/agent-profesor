namespace AgentProfesor.Core;

public enum DiffOpType
{
    Equal,
    Insert,
    Delete,
}

/// <summary>
/// Equal/Delete only carry a Count (the lines already exist in the base text being diffed
/// against, so storing them again would defeat the point of a "diff"). Insert carries Lines.
/// </summary>
public sealed record DiffOp(DiffOpType Type, int Count, IReadOnlyList<string>? Lines = null)
{
    public static DiffOp Equal(int count) => new(DiffOpType.Equal, count);
    public static DiffOp Delete(int count) => new(DiffOpType.Delete, count);
    public static DiffOp Insert(IReadOnlyList<string> lines) => new(DiffOpType.Insert, lines.Count, lines);
}

/// <summary>
/// Line-based Myers diff (the classic O(ND) shortest-edit-script algorithm).
/// </summary>
public static class LineDiff
{
    public static IReadOnlyList<DiffOp> Compute(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var trace = BuildTrace(a, b);
        return Backtrack(a, b, trace);
    }

    public static string[] Apply(IReadOnlyList<string> source, IReadOnlyList<DiffOp> ops)
    {
        var result = new List<string>();
        var srcIndex = 0;

        foreach (var op in ops)
        {
            switch (op.Type)
            {
                case DiffOpType.Equal:
                    for (var i = 0; i < op.Count; i++)
                        result.Add(source[srcIndex + i]);
                    srcIndex += op.Count;
                    break;
                case DiffOpType.Delete:
                    srcIndex += op.Count;
                    break;
                case DiffOpType.Insert:
                    result.AddRange(op.Lines!);
                    break;
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Fraction of the base (source) document touched by this diff. Used to decide whether
    /// storing a full keyframe would be cheaper/simpler than the diff itself.
    /// </summary>
    public static double ChangedRatio(IReadOnlyList<DiffOp> ops, int baseLineCount)
    {
        var changed = ops
            .Where(o => o.Type != DiffOpType.Equal)
            .Sum(o => o.Type == DiffOpType.Insert ? o.Lines!.Count : o.Count);

        if (baseLineCount == 0)
            return changed > 0 ? 1.0 : 0.0;

        return (double)changed / baseLineCount;
    }

    private static List<int[]> BuildTrace(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        int n = a.Count, m = b.Count;
        var max = n + m;
        var v = new int[2 * max + 3];
        var offset = max + 1;
        var trace = new List<int[]>();

        for (var d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());

            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                    x = v[offset + k + 1];
                else
                    x = v[offset + k - 1] + 1;

                var y = x - k;

                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;

                if (x >= n && y >= m)
                    return trace;
            }
        }

        return trace;
    }

    private static List<DiffOp> Backtrack(IReadOnlyList<string> a, IReadOnlyList<string> b, List<int[]> trace)
    {
        int x = a.Count, y = b.Count;
        var max = a.Count + b.Count;
        var offset = max + 1;
        var raw = new List<(DiffOpType Type, string? Line)>();

        for (var d = trace.Count - 1; d >= 0; d--)
        {
            var v = trace[d];
            var k = x - y;

            int prevK;
            if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                prevK = k + 1;
            else
                prevK = k - 1;

            var prevX = v[offset + prevK];
            var prevY = prevX - prevK;

            while (x > prevX && y > prevY)
            {
                raw.Add((DiffOpType.Equal, a[x - 1]));
                x--;
                y--;
            }

            if (d > 0)
            {
                if (x == prevX)
                {
                    raw.Add((DiffOpType.Insert, b[y - 1]));
                    y--;
                }
                else
                {
                    raw.Add((DiffOpType.Delete, a[x - 1]));
                    x--;
                }
            }
        }

        raw.Reverse();
        return Coalesce(raw);
    }

    private static List<DiffOp> Coalesce(List<(DiffOpType Type, string? Line)> raw)
    {
        var result = new List<DiffOp>();
        var pendingInsert = new List<string>();

        void FlushInsert()
        {
            if (pendingInsert.Count == 0)
                return;
            result.Add(DiffOp.Insert(new List<string>(pendingInsert)));
            pendingInsert.Clear();
        }

        foreach (var (type, line) in raw)
        {
            if (type == DiffOpType.Insert)
            {
                pendingInsert.Add(line!);
                continue;
            }

            FlushInsert();

            if (result.Count > 0 && result[^1].Type == type)
                result[^1] = type == DiffOpType.Equal
                    ? DiffOp.Equal(result[^1].Count + 1)
                    : DiffOp.Delete(result[^1].Count + 1);
            else
                result.Add(type == DiffOpType.Equal ? DiffOp.Equal(1) : DiffOp.Delete(1));
        }

        FlushInsert();
        return result;
    }
}

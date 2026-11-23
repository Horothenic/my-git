namespace MyGit.Core.History;

/// <summary>Which half of a row a line is drawn in.</summary>
public enum GraphHalf
{
    /// <summary>From the top edge of the row to its middle.</summary>
    Top,

    /// <summary>From the middle of the row to its bottom edge.</summary>
    Bottom,
}

/// <summary>A line in one half of a row, from lane <paramref name="From"/> to lane <paramref name="To"/>.</summary>
public readonly record struct GraphEdge(int From, int To, GraphHalf Half);

/// <summary>How to draw the graph next to one commit: the dot's lane and the lines around it.</summary>
public sealed record GraphRow(int NodeLane, IReadOnlyList<GraphEdge> Edges, int LaneCount);

/// <summary>
/// Assigns commits to vertical lanes and works out the connecting lines, one row at a time,
/// so a long history can be laid out page by page.
///
/// Each lane holds the commit it's waiting for next (a parent of a commit above). A commit goes
/// in the lane that's waiting for it; its first parent continues that lane, and further parents
/// (merges) get their own lanes. When several lanes wait for the same commit, they join into it.
/// </summary>
public sealed class GraphLayout
{
    private readonly List<string?> _lanes = [];

    /// <summary>Lays out the next commit (commits must come newest first, in topological order).</summary>
    public GraphRow Add(string sha, IReadOnlyList<string> parents)
    {
        var before = _lanes.ToList();
        var edges = new List<GraphEdge>();

        // 1. Find (or open) this commit's lane.
        var node = _lanes.IndexOf(sha);
        if (node < 0)
        {
            node = FreeLane(except: -1);
            Set(node, sha);
        }

        // 2. Top half: lanes coming in from above either end in this commit or pass straight through.
        for (var lane = 0; lane < before.Count; lane++)
        {
            if (before[lane] is { } waiting)
            {
                edges.Add(new GraphEdge(lane, waiting == sha ? node : lane, GraphHalf.Top));
            }
        }

        // 3. Every lane that waited for this commit is done.
        for (var lane = 0; lane < _lanes.Count; lane++)
        {
            if (_lanes[lane] == sha)
            {
                _lanes[lane] = null;
            }
        }

        // 4. Bottom half: lines down to the parents.
        for (var i = 0; i < parents.Count; i++)
        {
            var parent = parents[i];
            var existing = _lanes.IndexOf(parent);

            if (existing >= 0)
            {
                // Another lane already leads to this parent: join it.
                edges.Add(new GraphEdge(node, existing, GraphHalf.Bottom));
            }
            else
            {
                // The first parent continues this commit's lane; merge parents open new lanes.
                var lane = i == 0 && _lanes[node] is null ? node : FreeLane(except: node);
                Set(lane, parent);
                edges.Add(new GraphEdge(node, lane, GraphHalf.Bottom));
            }
        }

        // 5. Lanes that passed through on top keep going down.
        for (var lane = 0; lane < before.Count; lane++)
        {
            if (lane != node && before[lane] is { } waiting && waiting != sha && lane < _lanes.Count && _lanes[lane] == waiting)
            {
                edges.Add(new GraphEdge(lane, lane, GraphHalf.Bottom));
            }
        }

        var laneCount = Math.Max(before.Count, _lanes.Count);
        laneCount = Math.Max(laneCount, node + 1);

        // Drop empty lanes on the right so the graph doesn't grow wider than it needs to.
        while (_lanes.Count > 0 && _lanes[^1] is null)
        {
            _lanes.RemoveAt(_lanes.Count - 1);
        }

        return new GraphRow(node, edges, laneCount);
    }

    private int FreeLane(int except)
    {
        for (var lane = 0; lane < _lanes.Count; lane++)
        {
            if (lane != except && _lanes[lane] is null)
            {
                return lane;
            }
        }

        _lanes.Add(null);
        return _lanes.Count - 1;
    }

    private void Set(int lane, string sha)
    {
        while (_lanes.Count <= lane)
        {
            _lanes.Add(null);
        }

        _lanes[lane] = sha;
    }
}

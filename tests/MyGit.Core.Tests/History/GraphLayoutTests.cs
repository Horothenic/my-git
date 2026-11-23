using MyGit.Core.History;

namespace MyGit.Core.Tests.History;

public sealed class GraphLayoutTests
{
    [Fact]
    public void StraightHistory_StaysInOneLane()
    {
        var layout = new GraphLayout();

        var c = layout.Add("c", ["b"]);
        var b = layout.Add("b", ["a"]);
        var a = layout.Add("a", []);

        Assert.All(new[] { c, b, a }, row => Assert.Equal(0, row.NodeLane));
        Assert.Equal([new GraphEdge(0, 0, GraphHalf.Bottom)], c.Edges);                                         // newest: only down
        Assert.Equal([new GraphEdge(0, 0, GraphHalf.Top), new GraphEdge(0, 0, GraphHalf.Bottom)], b.Edges);     // middle: up and down
        Assert.Equal([new GraphEdge(0, 0, GraphHalf.Top)], a.Edges);                                            // first commit: only up
    }

    [Fact]
    public void Merge_OpensASecondLane_ThatJoinsBackAtTheForkPoint()
    {
        //   m      merge of f into main
        //   |\
        //   | f    feature commit
        //   b |    main commit
        //   |/
        //   a      where feature started
        var layout = new GraphLayout();

        var m = layout.Add("m", ["b", "f"]);
        Assert.Equal(0, m.NodeLane);
        Assert.Contains(new GraphEdge(0, 0, GraphHalf.Bottom), m.Edges);
        Assert.Contains(new GraphEdge(0, 1, GraphHalf.Bottom), m.Edges); // line out to the merged branch

        var b = layout.Add("b", ["a"]);
        Assert.Equal(0, b.NodeLane);
        Assert.Contains(new GraphEdge(1, 1, GraphHalf.Top), b.Edges);    // feature lane passes by
        Assert.Contains(new GraphEdge(1, 1, GraphHalf.Bottom), b.Edges);

        var f = layout.Add("f", ["a"]);
        Assert.Equal(1, f.NodeLane);
        Assert.Contains(new GraphEdge(1, 0, GraphHalf.Bottom), f.Edges); // joins main's lane, which waits for "a"

        var a = layout.Add("a", []);
        Assert.Equal(0, a.NodeLane);
        Assert.Equal(1, a.LaneCount);
    }

    [Fact]
    public void TwoBranchHeads_GetTheirOwnLanes()
    {
        // Two tips (e.g. HEAD and its upstream) that share a parent.
        var layout = new GraphLayout();

        var mine = layout.Add("mine", ["base"]);
        var theirs = layout.Add("theirs", ["base"]);
        var shared = layout.Add("base", []);

        Assert.Equal(0, mine.NodeLane);
        Assert.Equal(1, theirs.NodeLane);
        Assert.Contains(new GraphEdge(1, 0, GraphHalf.Bottom), theirs.Edges);
        Assert.Equal(0, shared.NodeLane);
    }
}

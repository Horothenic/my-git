using MyGit.Core.Clone;

namespace MyGit.Core.Tests.Clone;

public sealed class CloneStageTrackerTests
{
    [Fact]
    public void Apply_MarksCurrentStageActive_WithDetail()
    {
        var tracker = new CloneStageTracker();

        tracker.Apply(CloneProgress.Parse("Receiving objects:  64% (1290/2015), 41.20 MiB | 10.30 MiB/s")!);

        var receiving = tracker.Stages[2];
        Assert.Equal(CloneStageState.Active, receiving.State);
        Assert.Equal(64, receiving.Percent);
        Assert.Equal("64%  ·  41.20 MiB  ·  10.30 MiB/s", receiving.Detail);
        Assert.Same(receiving, tracker.Current);
    }

    [Fact]
    public void Apply_LaterStage_FinishesSkippedEarlierStages()
    {
        var tracker = new CloneStageTracker();

        // Small repos often go straight from counting to receiving: no "Compressing objects" line.
        tracker.Apply(CloneProgress.Parse("remote: Counting objects: 100% (12/12), done.")!);
        tracker.Apply(CloneProgress.Parse("Receiving objects:  10% (1/12)")!);

        Assert.Equal(CloneStageState.Done, tracker.Stages[0].State);
        Assert.Equal("12", tracker.Stages[0].Detail);
        Assert.Equal(CloneStageState.Done, tracker.Stages[1].State);
        Assert.Equal(CloneStageState.Active, tracker.Stages[2].State);
        Assert.Equal(CloneStageState.Pending, tracker.Stages[3].State);
    }

    [Fact]
    public void Apply_IgnoresUnknownPhases()
    {
        var tracker = new CloneStageTracker();

        Assert.False(tracker.Apply(new CloneProgress("Enumerating objects", 50, null)));
        Assert.All(tracker.Stages, stage => Assert.Equal(CloneStageState.Pending, stage.State));
    }

    [Fact]
    public void CompleteAll_MarksEverythingDone()
    {
        var tracker = new CloneStageTracker();
        tracker.Apply(CloneProgress.Parse("Receiving objects:  64% (1290/2015)")!);

        tracker.CompleteAll();

        Assert.All(tracker.Stages, stage => Assert.Equal(CloneStageState.Done, stage.State));
        Assert.Null(tracker.Current);
    }
}

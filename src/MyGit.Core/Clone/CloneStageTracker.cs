namespace MyGit.Core.Clone;

public enum CloneStageState
{
    Pending,
    Active,
    Done,
}

/// <summary>One row of the clone checklist.</summary>
public sealed class CloneStage(string gitPhase, string displayName)
{
    /// <summary>The phase name git prints, e.g. "Receiving objects".</summary>
    public string GitPhase { get; } = gitPhase;

    /// <summary>What the checklist shows, e.g. "Checking out files" for git's "Updating files".</summary>
    public string DisplayName { get; } = displayName;

    public CloneStageState State { get; internal set; } = CloneStageState.Pending;

    public int? Percent { get; internal set; }

    /// <summary>Short status for the right side of the row: a count, size and speed, or "done".</summary>
    public string? Detail { get; internal set; }
}

/// <summary>
/// Turns git clone progress lines into a fixed checklist of stages.
/// Git skips stages for some repositories (small ones often have no "Compressing"),
/// so reaching a later stage marks every earlier one as done.
/// </summary>
public sealed class CloneStageTracker
{
    public IReadOnlyList<CloneStage> Stages { get; } =
    [
        new("Counting objects", "Counting objects"),
        new("Compressing objects", "Compressing objects"),
        new("Receiving objects", "Receiving objects"),
        new("Resolving deltas", "Resolving deltas"),
        new("Updating files", "Checking out files"),
    ];

    /// <summary>The stage git is working on now, or null before the first update.</summary>
    public CloneStage? Current => Stages.FirstOrDefault(stage => stage.State == CloneStageState.Active);

    /// <summary>Applies one progress update. Returns false if the phase isn't one of the stages.</summary>
    public bool Apply(CloneProgress progress)
    {
        var index = IndexOf(progress.Phase);
        if (index < 0)
        {
            return false;
        }

        for (var i = 0; i < index; i++)
        {
            Finish(Stages[i]);
        }

        var stage = Stages[index];
        stage.Percent = progress.Percent;

        if (progress.Percent >= 100)
        {
            Finish(stage, progress);
        }
        else
        {
            stage.State = CloneStageState.Active;
            stage.Detail = Describe(progress);
        }

        return true;
    }

    /// <summary>Marks every stage done. Call when the clone succeeded.</summary>
    public void CompleteAll()
    {
        foreach (var stage in Stages)
        {
            Finish(stage);
        }
    }

    private int IndexOf(string phase)
    {
        for (var i = 0; i < Stages.Count; i++)
        {
            if (string.Equals(Stages[i].GitPhase, phase, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static void Finish(CloneStage stage, CloneProgress? last = null)
    {
        if (stage.State == CloneStageState.Done)
        {
            return;
        }

        stage.State = CloneStageState.Done;
        stage.Percent = 100;
        stage.Detail = last?.Total is { } total ? total.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : "done";
    }

    private static string? Describe(CloneProgress progress)
    {
        var parts = new List<string>();

        if (progress.Percent is { } percent)
        {
            parts.Add($"{percent}%");
        }

        if (!string.IsNullOrEmpty(progress.Detail))
        {
            // git prints "41.20 MiB | 10.30 MiB/s"
            parts.AddRange(progress.Detail.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
        }
        else if (progress.Current is { } current && progress.Total is { } total)
        {
            parts.Add($"{current:N0} / {total:N0}");
        }

        return parts.Count == 0 ? null : string.Join("  ·  ", parts);
    }
}

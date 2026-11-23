using CommunityToolkit.Mvvm.ComponentModel;
using MyGit.Core.Clone;

namespace MyGit.App.ViewModels;

/// <summary>
/// What the clone window is showing.
/// </summary>
public enum CloneMode
{
    /// <summary>Choosing a repository and folder.</summary>
    Form,

    /// <summary>The clone is running: the stage checklist.</summary>
    Cloning,

    /// <summary>Cancel was pressed; waiting for git to stop and the folder to be removed.</summary>
    Stopping,

    /// <summary>The clone failed: reason plus Back / Try again.</summary>
    Failed,
}

/// <summary>
/// One row of the clone checklist, mirroring a <see cref="CloneStage"/> for binding.
/// </summary>
public sealed partial class CloneStageViewModel : ObservableObject
{
    public CloneStageViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsActive), nameof(IsDone))]
    public partial CloneStageState State { get; set; }

    public bool IsPending => State == CloneStageState.Pending;

    public bool IsActive => State == CloneStageState.Active;

    public bool IsDone => State == CloneStageState.Done;

    [ObservableProperty]
    public partial double Percent { get; set; }

    [ObservableProperty]
    public partial string? Detail { get; set; }

    public void Update(CloneStage stage)
    {
        State = stage.State;
        Percent = stage.Percent ?? 0;
        Detail = stage.Detail;
    }
}

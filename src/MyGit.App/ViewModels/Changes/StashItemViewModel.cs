using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using MyGit.Core.Stashes;

namespace MyGit.App.ViewModels.Changes;

/// <summary>One stash in the Changes tab's Stashes section. Its commands call back into the tab.</summary>
public sealed partial class StashItemViewModel(StashEntry stash, ChangesViewModel owner)
{
    public StashEntry Stash { get; } = stash;

    public string Message => Stash.Message;

    public string Details => $"{Stash.Branch} · {Describe(Stash.CreatedAt)}";

    public string ToolTip => $"{Stash.Message}\nMade on {Stash.Branch}, {Stash.CreatedAt.ToString("f", CultureInfo.CurrentCulture)}";

    [RelayCommand]
    private Task ApplyAsync() => owner.ApplyStashAsync(this, remove: false);

    [RelayCommand]
    private Task PopAsync() => owner.ApplyStashAsync(this, remove: true);

    [RelayCommand]
    private Task DropAsync() => owner.DropStashAsync(this);

    private static string Describe(DateTimeOffset when)
    {
        var age = DateTimeOffset.Now - when;
        return age switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)age.TotalMinutes} min",
            { TotalHours: < 24 } => $"{(int)age.TotalHours} h",
            _ => $"{(int)age.TotalDays} d",
        };
    }
}

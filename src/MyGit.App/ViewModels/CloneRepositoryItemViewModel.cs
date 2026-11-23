using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.GitHub.Repositories;

namespace MyGit.App.ViewModels;

/// <summary>
/// One GitHub repository in the clone dialog list.
/// </summary>
public sealed partial class CloneRepositoryItemViewModel : ObservableObject
{
    private readonly Action<CloneRepositoryItemViewModel> _select;

    public CloneRepositoryItemViewModel(RemoteRepository repository, bool isCloned, Action<CloneRepositoryItemViewModel> select)
    {
        Repository = repository;
        IsCloned = isCloned;
        _select = select;
    }

    public RemoteRepository Repository { get; }

    public string Name => Repository.Name;

    public bool IsPrivate => Repository.IsPrivate;

    /// <summary>True when a repository in your list already has this remote.</summary>
    public bool IsCloned { get; }

    public string PushedText => Repository.PushedAt is { } pushed ? $"pushed {Describe(DateTimeOffset.Now - pushed)}" : string.Empty;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => _select(this);

    private static string Describe(TimeSpan age) => age switch
    {
        { TotalMinutes: < 60 } => $"{Math.Max(1, (int)age.TotalMinutes)}m ago",
        { TotalHours: < 24 } => $"{(int)age.TotalHours}h ago",
        { TotalDays: < 30 } => $"{(int)age.TotalDays}d ago",
        { TotalDays: < 365 } => $"{(int)(age.TotalDays / 30)}mo ago",
        _ => $"{(int)(age.TotalDays / 365)}y ago",
    };
}

/// <summary>
/// Repositories of one owner (you or an organization) in the clone dialog.
/// </summary>
public sealed class CloneRepositoryGroupViewModel(string owner, IReadOnlyList<CloneRepositoryItemViewModel> items)
{
    public string Owner { get; } = owner;

    public IReadOnlyList<CloneRepositoryItemViewModel> Items { get; } = items;
}

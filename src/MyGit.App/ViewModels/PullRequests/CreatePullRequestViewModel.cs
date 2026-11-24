using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyGit.Core.PullRequests;
using MyGit.GitHub.PullRequests;

namespace MyGit.App.ViewModels.PullRequests;

/// <summary>A choice in the Template picker; <see cref="Content"/> is null for "None (commit list)".</summary>
public sealed record TemplateOption(string Name, string? Content)
{
    public override string ToString() => Name;
}

/// <summary>
/// The Create pull request dialog: base branch, title, description (from the repo's PR template
/// or the commit list), draft. Title and description follow the base until you edit them.
/// </summary>
public sealed partial class CreatePullRequestViewModel : ObservableObject
{
    private static readonly TemplateOption NoTemplate = new("None (commit list)", null);

    private readonly Func<string, Task<(IReadOnlyList<string> Subjects, IReadOnlyList<PullRequestTemplate> Templates)>> _loadDraft;
    private readonly Func<string, string, string, bool, Task<PullRequestInfo>> _create;

    private IReadOnlyList<string> _subjects = [];
    private string _generatedTitle = string.Empty;
    private string _generatedBody = string.Empty;
    private bool _quiet;

    /// <param name="loadDraft">For a base branch: the commits it doesn't have, and its PR templates.</param>
    /// <param name="create">Creates the PR on GitHub: (base, title, body, draft).</param>
    public CreatePullRequestViewModel(
        string head,
        IReadOnlyList<string> bases,
        string defaultBase,
        Func<string, Task<(IReadOnlyList<string> Subjects, IReadOnlyList<PullRequestTemplate> Templates)>> loadDraft,
        Func<string, string, string, bool, Task<PullRequestInfo>> create)
    {
        Head = head;
        _loadDraft = loadDraft;
        _create = create;
        foreach (var name in bases)
        {
            Bases.Add(name);
        }

        _quiet = true;
        SelectedBase = defaultBase;
        _quiet = false;
    }

    /// <summary>Raised when the window should close.</summary>
    public event Action? Finished;

    /// <summary>Asks before a template replaces a description you typed. Set by the window.</summary>
    public Func<Task<bool>>? ConfirmReplaceDescription { get; set; }

    public string Head { get; }

    /// <summary>The dialog's heading, e.g. "Pull request 1 of 3: inventory-model" when creating a stack's PRs.</summary>
    public string Heading { get; init; } = "Create pull request";

    /// <summary>Creating a stack's PRs one by one: Skip goes on to the next one.</summary>
    public bool CanSkip { get; init; }

    /// <summary>Set when Skip was pressed.</summary>
    public bool Skipped { get; private set; }

    /// <summary>A stack's PR goes into the branch below; the base is only a choice for a single PR.</summary>
    public bool CanChooseBase => Bases.Count > 1;

    public ObservableCollection<string> Bases { get; } = [];

    [ObservableProperty]
    public partial string? SelectedBase { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Body { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDraft { get; set; }

    public ObservableCollection<TemplateOption> Templates { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateSelected))]
    public partial TemplateOption? SelectedTemplate { get; set; }

    /// <summary>The picker shows only when there's more than one template to choose from.</summary>
    [ObservableProperty]
    public partial bool HasTemplateChoice { get; set; }

    /// <summary>A template fills the description; "Insert commit list" adds the commits into it.</summary>
    public bool IsTemplateSelected => SelectedTemplate?.Content is not null;

    /// <summary>e.g. "3 commits".</summary>
    [ObservableProperty]
    public partial string CommitSummary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorText { get; set; } = string.Empty;

    public bool HasError => ErrorText.Length > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>Set once GitHub created the pull request.</summary>
    public PullRequestInfo? Created { get; private set; }

    /// <summary>The commits as a list, for "Insert commit list".</summary>
    public string CommitList => PullRequestDraftService.CommitList(_subjects);

    /// <summary>Loads the commits and templates for the selected base. Call before showing the dialog.</summary>
    public async Task LoadAsync()
    {
        if (SelectedBase is not { } @base)
        {
            return;
        }

        var (subjects, templates) = await _loadDraft(@base);
        _subjects = subjects;
        CommitSummary = subjects.Count == 1 ? "1 commit" : $"{subjects.Count} commits";

        if (Title == _generatedTitle)
        {
            Title = _generatedTitle = PullRequestDraftService.TitleFor(Head, subjects);
        }

        var keepBody = Body != _generatedBody;
        _quiet = true;
        Templates.Clear();
        Templates.Add(NoTemplate);
        foreach (var template in templates)
        {
            Templates.Add(new TemplateOption(template.Name, template.Content));
        }

        HasTemplateChoice = templates.Count > 1;
        SelectedTemplate = templates.Count > 0 ? Templates[1] : NoTemplate;
        _quiet = false;

        if (!keepBody)
        {
            Body = _generatedBody = BodyFor(SelectedTemplate);
        }
    }

    /// <summary>Inserts the commit list at <paramref name="caret"/>; returns where the caret goes after it.</summary>
    public int InsertCommitList(int caret)
    {
        caret = Math.Clamp(caret, 0, Body.Length);
        var before = caret > 0 && Body[caret - 1] != '\n' ? "\n" : string.Empty;
        var insert = before + CommitList + "\n";
        Body = Body.Insert(caret, insert);
        return caret + insert.Length;
    }

    partial void OnSelectedBaseChanged(string? value)
    {
        if (!_quiet)
        {
            _ = LoadAsync();
        }
    }

    partial void OnSelectedTemplateChanged(TemplateOption? oldValue, TemplateOption? newValue)
    {
        if (!_quiet && newValue is not null)
        {
            _ = SwitchTemplateAsync(oldValue, newValue);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (SelectedBase is not { } @base)
        {
            return;
        }

        IsBusy = true;
        ErrorText = string.Empty;
        try
        {
            Created = await _create(@base, Title.Trim(), Body, IsDraft);
            Finished?.Invoke();
        }
        catch (Exception exception) when (exception is PullRequestException or Octokit.ApiException or HttpRequestException or InvalidOperationException)
        {
            ErrorText = $"GitHub didn't create it: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => Finished?.Invoke();

    [RelayCommand]
    private void Skip()
    {
        Skipped = true;
        Finished?.Invoke();
    }

    private bool CanCreate() => !IsBusy && Title.Trim().Length > 0;

    private async Task SwitchTemplateAsync(TemplateOption? previous, TemplateOption next)
    {
        var edited = Body != _generatedBody && Body.Trim().Length > 0;
        if (edited && ConfirmReplaceDescription is { } confirm && !await confirm())
        {
            _quiet = true;
            SelectedTemplate = previous;
            _quiet = false;
            return;
        }

        Body = _generatedBody = BodyFor(next);
    }

    private string BodyFor(TemplateOption? template) => template?.Content ?? CommitList;
}

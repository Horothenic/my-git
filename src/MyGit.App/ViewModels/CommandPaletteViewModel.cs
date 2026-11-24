using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;

namespace MyGit.App.ViewModels;

/// <summary>One thing the palette can do: an action, a branch to switch to, a repository to open.</summary>
/// <param name="Shortcut">Shown on the right, e.g. "Ctrl+Shift+F".</param>
/// <param name="Hint">Shown on the right when there's no shortcut, e.g. "branch".</param>
public sealed record PaletteItem(string Title, MaterialIconKind Icon, ICommand Command, object? Parameter = null, string? Shortcut = null, string? Hint = null)
{
    public bool CanRun => Command.CanExecute(Parameter);

    /// <summary>Things that can't run right now (e.g. Push with nothing to push) are shown faded.</summary>
    public double Opacity => CanRun ? 1 : 0.45;

    public string Right => Shortcut is { } shortcut ? Services.Hotkeys.Show(shortcut) : Hint ?? string.Empty;

    public bool HasShortcut => Shortcut is not null;
}

/// <summary>
/// The command palette (Ctrl+Shift+P): type to filter everything My Git can do, Enter to run it.
/// Every word typed must appear in the title; titles that start with it come first.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private IReadOnlyList<PaletteItem> _all = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    public ObservableCollection<PaletteItem> Items { get; } = [];

    [ObservableProperty]
    public partial PaletteItem? SelectedItem { get; set; }

    public void Open(IReadOnlyList<PaletteItem> items)
    {
        _all = items;
        Query = string.Empty;
        Filter();
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    /// <summary>Runs the selected item (if it can run now) and closes.</summary>
    public void RunSelected()
    {
        if (SelectedItem is not { CanRun: true } item)
        {
            return;
        }

        Close();
        item.Command.Execute(item.Parameter);
    }

    /// <summary>Moves the selection by <paramref name="by"/> rows (↑ / ↓).</summary>
    public void Move(int by)
    {
        if (Items.Count == 0)
        {
            return;
        }

        var index = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        SelectedItem = Items[Math.Clamp(index + by, 0, Items.Count - 1)];
    }

    partial void OnQueryChanged(string value) => Filter();

    private void Filter()
    {
        var words = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _all
            .Where(item => words.All(word => item.Title.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => words.Length > 0 && item.Title.StartsWith(words[0], StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => item.CanRun)
            .Take(60)
            .ToList();

        Items.Clear();
        foreach (var item in matches)
        {
            Items.Add(item);
        }

        SelectedItem = Items.FirstOrDefault(item => item.CanRun) ?? Items.FirstOrDefault();
    }
}

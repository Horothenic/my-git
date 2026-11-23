using MyGit.Core.Changes;
using MyGit.Core.Diffs;
using MyGit.Core.Git;

namespace MyGit.Core.Tests.Changes;

public sealed class PartialChangesServiceTests
{
    private readonly GitRunner _git = new();
    private readonly ChangesService _changes;
    private readonly DiffService _diffs;
    private readonly PartialChangesService _partial;

    public PartialChangesServiceTests()
    {
        _changes = new ChangesService(_git, File.Delete);
        _diffs = new DiffService(_git);
        _partial = new PartialChangesService(_git, _changes, _diffs);
    }

    // 20 numbered lines, so two edits far apart become two separate hunks.
    private static string Lines(params (int Line, string Text)[] replacements)
    {
        var lines = Enumerable.Range(1, 20).Select(i => $"line {i}").ToArray();
        foreach (var (line, text) in replacements)
        {
            lines[line - 1] = text;
        }

        return string.Join("\n", lines) + "\n";
    }

    private static async Task<TempRepository> RepoWithFileAsync(string committed, string working, string name = "file.txt")
    {
        var repository = await TempRepository.CreateAsync();
        await repository.RunAsync("config", "core.autocrlf", "false");
        await repository.CommitFileAsync(name, committed, "first");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, name), working);
        return repository;
    }

    private Task<FileDiff> DiffAsync(TempRepository repository, FileChange change, bool staged) =>
        _diffs.GetDiffAsync(repository.Path, change, staged);

    private static async Task<string> IndexContentAsync(TempRepository repository, string name = "file.txt") =>
        (await repository.RunAsync("show", $":{name}")).StandardOutput;

    private static readonly FileChange Modified = new("file.txt", ChangeKind.Modified);

    [Fact]
    public async Task StageHunk_StagesOnlyThatHunk()
    {
        using var repository = await RepoWithFileAsync(Lines(), Lines((2, "TWO"), (18, "EIGHTEEN")));
        var diff = await DiffAsync(repository, Modified, staged: false);
        Assert.Equal(2, diff.Hunks.Count);

        await _partial.StageAsync(repository.Path, Modified, diff, PatchBuilder.AllChanges(diff, onlyHunk: 1));

        Assert.Equal(Lines((18, "EIGHTEEN")), await IndexContentAsync(repository));
        Assert.Equal(Lines((2, "TWO"), (18, "EIGHTEEN")), await File.ReadAllTextAsync(Path.Combine(repository.Path, "file.txt")));
    }

    [Fact]
    public async Task StageLines_OnlyTheAddition_KeepsTheOldLineInTheIndex()
    {
        // Line 2 edited: "-line 2" / "+TWO". Stage only the "+TWO" line.
        using var repository = await RepoWithFileAsync(Lines(), Lines((2, "TWO")));
        var diff = await DiffAsync(repository, Modified, staged: false);
        var added = FindLine(diff, DiffLineKind.Added, "TWO");

        await _partial.StageAsync(repository.Path, Modified, diff, new HashSet<DiffLineRef> { added });

        var index = await IndexContentAsync(repository);
        Assert.Contains("line 2\nTWO\nline 3", index); // old line kept, new line added
    }

    [Fact]
    public async Task StageLines_OneOfSeveralAddedLines()
    {
        var working = Lines().Replace("line 5\n", "line 5\nnew A\nnew B\nnew C\n");
        using var repository = await RepoWithFileAsync(Lines(), working);
        var diff = await DiffAsync(repository, Modified, staged: false);

        await _partial.StageAsync(repository.Path, Modified, diff, new HashSet<DiffLineRef> { FindLine(diff, DiffLineKind.Added, "new B") });

        Assert.Equal(Lines().Replace("line 5\n", "line 5\nnew B\n"), await IndexContentAsync(repository));
    }

    [Fact]
    public async Task UnstageLines_FromTheStagedDiff()
    {
        using var repository = await RepoWithFileAsync(Lines(), Lines((2, "TWO"), (18, "EIGHTEEN")));
        await repository.RunAsync("add", "file.txt");
        var staged = await DiffAsync(repository, Modified, staged: true);

        await _partial.UnstageAsync(repository.Path, Modified, staged, PatchBuilder.AllChanges(staged, onlyHunk: 0));

        Assert.Equal(Lines((18, "EIGHTEEN")), await IndexContentAsync(repository));
        Assert.Equal(Lines((2, "TWO"), (18, "EIGHTEEN")), await File.ReadAllTextAsync(Path.Combine(repository.Path, "file.txt")));
    }

    [Fact]
    public async Task DiscardHunk_ResetsOnlyThatPartOfTheFile()
    {
        using var repository = await RepoWithFileAsync(Lines(), Lines((2, "TWO"), (18, "EIGHTEEN")));
        var diff = await DiffAsync(repository, Modified, staged: false);

        await _partial.DiscardAsync(repository.Path, Modified, diff, PatchBuilder.AllChanges(diff, onlyHunk: 0));

        Assert.Equal(Lines((18, "EIGHTEEN")), await File.ReadAllTextAsync(Path.Combine(repository.Path, "file.txt")));
    }

    [Fact]
    public async Task StageLines_KeepsWindowsLineEndings()
    {
        static string Crlf(string text) => text.Replace("\n", "\r\n");
        using var repository = await RepoWithFileAsync(Crlf(Lines()), Crlf(Lines((2, "TWO"), (18, "EIGHTEEN"))));
        var diff = await DiffAsync(repository, Modified, staged: false);

        await _partial.StageAsync(repository.Path, Modified, diff, PatchBuilder.AllChanges(diff, onlyHunk: 0));

        Assert.Equal(Crlf(Lines((2, "TWO"))), await IndexContentAsync(repository));
    }

    [Fact]
    public async Task StageLines_HandlesMissingNewlineAtEnd()
    {
        using var repository = await RepoWithFileAsync("a\nb\nc", "a\nB\nc\nd");
        var diff = await DiffAsync(repository, Modified, staged: false);

        await _partial.StageAsync(repository.Path, Modified, diff, new HashSet<DiffLineRef>
        {
            FindLine(diff, DiffLineKind.Removed, "b"),
            FindLine(diff, DiffLineKind.Added, "B"),
        });

        Assert.Equal("a\nB\nc", await IndexContentAsync(repository));
    }

    [Fact]
    public async Task StageLines_OfANewFile_CreatesItWithJustThoseLines()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("other.txt", "x", "first");
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "new.txt"), "keep 1\nskip\nkeep 2\n");
        var change = new FileChange("new.txt", ChangeKind.Untracked);
        var diff = await DiffAsync(repository, change, staged: false);

        await _partial.StageAsync(repository.Path, change, diff, new HashSet<DiffLineRef>
        {
            FindLine(diff, DiffLineKind.Added, "keep 1"),
            FindLine(diff, DiffLineKind.Added, "keep 2"),
        });

        Assert.Equal("keep 1\nkeep 2\n", await IndexContentAsync(repository, "new.txt"));
    }

    [Fact]
    public async Task DiscardLines_OfANewFile_RemovesThemFromTheFile()
    {
        using var repository = await TempRepository.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "new.txt"), "keep\nremove me\nkeep too\n");
        var change = new FileChange("new.txt", ChangeKind.Untracked);
        var diff = await DiffAsync(repository, change, staged: false);

        await _partial.DiscardAsync(repository.Path, change, diff, new HashSet<DiffLineRef> { FindLine(diff, DiffLineKind.Added, "remove me") });

        Assert.Equal("keep\nkeep too\n", await File.ReadAllTextAsync(Path.Combine(repository.Path, "new.txt")));
    }

    [Fact]
    public async Task SelectingEverything_UsesTheWholeFileCommand()
    {
        using var repository = await RepoWithFileAsync(Lines(), Lines((2, "TWO")));
        File.Delete(Path.Combine(repository.Path, "file.txt"));
        var deleted = new FileChange("file.txt", ChangeKind.Deleted);
        var diff = await DiffAsync(repository, deleted, staged: false);

        await _partial.StageAsync(repository.Path, deleted, diff, PatchBuilder.AllChanges(diff));

        var status = await _changes.GetStatusAsync(repository.Path);
        Assert.Equal(ChangeKind.Deleted, Assert.Single(status.Staged).Kind); // a real deletion, not an empty file
    }

    [Fact]
    public async Task Stage_Refuses_WhenTheFileChangedSinceTheDiffWasShown()
    {
        using var repository = await RepoWithFileAsync(Lines(), Lines((2, "TWO")));
        var diff = await DiffAsync(repository, Modified, staged: false);
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "file.txt"), Lines((2, "TWO"), (3, "unity saved this")));

        await Assert.ThrowsAsync<StaleDiffException>(() =>
            _partial.StageAsync(repository.Path, Modified, diff, PatchBuilder.AllChanges(diff, onlyHunk: 0)));
    }

    private static DiffLineRef FindLine(FileDiff diff, DiffLineKind kind, string text)
    {
        for (var hunk = 0; hunk < diff.Hunks.Count; hunk++)
        {
            var lines = diff.Hunks[hunk].Lines;
            for (var line = 0; line < lines.Count; line++)
            {
                if (lines[line].Kind == kind && lines[line].Text == text)
                {
                    return new DiffLineRef(hunk, line);
                }
            }
        }

        throw new InvalidOperationException($"No {kind} line '{text}' in the diff.");
    }
}

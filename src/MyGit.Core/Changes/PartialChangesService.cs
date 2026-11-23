using System.Text;
using MyGit.Core.Diffs;
using MyGit.Core.Git;

namespace MyGit.Core.Changes;

/// <summary>
/// Thrown when the file changed on disk after its diff was shown, so the selection no longer
/// matches. The caller reloads the diff and lets the user pick again.
/// </summary>
public sealed class StaleDiffException() : Exception("The file changed since its diff was shown. Pick the lines again.");

/// <summary>
/// Stages, unstages and discards single hunks or lines (see <see cref="PatchBuilder"/>).
/// When the selection covers every change in the file, the whole-file commands are used instead.
/// </summary>
public sealed class PartialChangesService
{
    private readonly IGitRunner _git;
    private readonly ChangesService _changes;
    private readonly DiffService _diffs;

    public PartialChangesService(IGitRunner git, ChangesService changes, DiffService diffs)
    {
        _git = git;
        _changes = changes;
        _diffs = diffs;
    }

    /// <summary>True when line-level actions make sense for this change.</summary>
    public static bool Supports(FileChange change) =>
        change.Kind is not (ChangeKind.Renamed or ChangeKind.Copied or ChangeKind.Conflicted or ChangeKind.TypeChanged);

    /// <summary>Unstaged → Staged, for the selected lines of <paramref name="shown"/> (the Unstaged diff).</summary>
    public async Task StageAsync(string repositoryPath, FileChange change, FileDiff shown, IReadOnlySet<DiffLineRef> selection, CancellationToken cancellationToken = default)
    {
        var current = await EnsureCurrentAsync(repositoryPath, change, shown, staged: false, cancellationToken);

        if (CoversEverything(current, selection))
        {
            await _changes.StageAsync(repositoryPath, [change.Path], cancellationToken);
            return;
        }

        var patch = PatchBuilder.Build(current, selection, reverse: false, isNewFile: change.Kind == ChangeKind.Untracked);
        await ApplyAsync(repositoryPath, patch, ["--cached"], cancellationToken);
    }

    /// <summary>Staged → Unstaged, for the selected lines of <paramref name="shown"/> (the Staged diff).</summary>
    public async Task UnstageAsync(string repositoryPath, FileChange change, FileDiff shown, IReadOnlySet<DiffLineRef> selection, CancellationToken cancellationToken = default)
    {
        var current = await EnsureCurrentAsync(repositoryPath, change, shown, staged: true, cancellationToken);

        if (CoversEverything(current, selection))
        {
            await _changes.UnstageAsync(repositoryPath, [change.Path], cancellationToken);
            return;
        }

        var patch = PatchBuilder.Build(current, selection, reverse: true);
        await ApplyAsync(repositoryPath, patch, ["--cached", "--reverse"], cancellationToken);
    }

    /// <summary>Throws away the selected lines of <paramref name="shown"/> (the Unstaged diff) from the working file.</summary>
    public async Task DiscardAsync(string repositoryPath, FileChange change, FileDiff shown, IReadOnlySet<DiffLineRef> selection, CancellationToken cancellationToken = default)
    {
        var current = await EnsureCurrentAsync(repositoryPath, change, shown, staged: false, cancellationToken);

        if (CoversEverything(current, selection))
        {
            await _changes.DiscardAsync(repositoryPath, [change], cancellationToken);
            return;
        }

        if (change.Kind == ChangeKind.Untracked)
        {
            // A new file has nothing to go back to: remove the selected lines from it directly.
            await RemoveLinesFromNewFileAsync(repositoryPath, change.Path, current, selection, cancellationToken);
            return;
        }

        var patch = PatchBuilder.Build(current, selection, reverse: true);
        await ApplyAsync(repositoryPath, patch, ["--reverse"], cancellationToken);
    }

    private async Task<FileDiff> EnsureCurrentAsync(string repositoryPath, FileChange change, FileDiff shown, bool staged, CancellationToken cancellationToken)
    {
        // The patch must be built from exactly what the user saw. If the file changed meanwhile
        // (Unity saved it, say), stop and let the user pick again.
        var current = await _diffs.GetDiffAsync(repositoryPath, change, staged, cancellationToken);

        if (current.RawText != shown.RawText)
        {
            throw new StaleDiffException();
        }

        return current;
    }

    private static bool CoversEverything(FileDiff diff, IReadOnlySet<DiffLineRef> selection) =>
        PatchBuilder.AllChanges(diff).All(selection.Contains);

    private async Task ApplyAsync(string repositoryPath, string? patch, IReadOnlyList<string> options, CancellationToken cancellationToken)
    {
        if (patch is null)
        {
            return;
        }

        var patchFile = Path.GetTempFileName();

        try
        {
            // UTF-8 without a byte-order mark, exactly the bytes git gave us.
            await File.WriteAllTextAsync(patchFile, patch, new UTF8Encoding(false), cancellationToken);

            // --recount: trust the lines, not the counts in "@@" headers.
            await _git.RunCheckedAsync(
                repositoryPath,
                ["apply", .. options, "--recount", "--whitespace=nowarn", patchFile],
                cancellationToken: cancellationToken);
        }
        finally
        {
            File.Delete(patchFile);
        }
    }

    private static async Task RemoveLinesFromNewFileAsync(string repositoryPath, string path, FileDiff diff, IReadOnlySet<DiffLineRef> selection, CancellationToken cancellationToken)
    {
        var fullPath = Path.Combine(repositoryPath, path.Replace('/', Path.DirectorySeparatorChar));
        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var endsWithNewline = content.EndsWith('\n');

        var kept = diff.Hunks[0].Lines
            .Where((_, index) => !selection.Contains(new DiffLineRef(0, index)))
            .Select(line => line.Text);

        var result = string.Join(newline, kept);
        if (endsWithNewline && result.Length > 0)
        {
            result += newline;
        }

        await File.WriteAllTextAsync(fullPath, result, new UTF8Encoding(false), cancellationToken);
    }
}

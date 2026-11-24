using Microsoft.VisualBasic.FileIO;
using MyGit.Core.Git;

namespace MyGit.Core.Changes;

/// <summary>
/// Reads the working tree status and moves files between Unstaged and Staged.
/// </summary>
public sealed class ChangesService
{
    // Windows limits a command line to about 32,000 characters, so long path lists go in batches.
    private const int PathsPerCommand = 100;

    private readonly IGitRunner _git;
    private readonly Action<string> _deleteUntrackedFile;

    /// <param name="deleteUntrackedFile">
    /// How discarded untracked files are removed. Defaults to the Recycle Bin; tests delete directly.
    /// </param>
    public ChangesService(IGitRunner git, Action<string>? deleteUntrackedFile = null)
    {
        _git = git;
        _deleteUntrackedFile = deleteUntrackedFile ?? MoveToRecycleBin;
    }

    private static void MoveToRecycleBin(string fullPath) =>
        FileSystem.DeleteFile(fullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

    public async Task<WorkingTreeStatus> GetStatusAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        // --no-optional-locks: a background refresh must never block git running elsewhere (IDE, Unity).
        var result = await _git.RunCheckedAsync(
            repositoryPath,
            ["--no-optional-locks", "status", "--porcelain=v2", "-z", "--untracked-files=all"],
            cancellationToken: cancellationToken);

        return StatusParser.Parse(result.StandardOutput);
    }

    /// <summary>Stages files: new, modified and deleted ones alike.</summary>
    public Task StageAsync(string repositoryPath, IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default) =>
        RunInBatchesAsync(repositoryPath, ["add", "--all"], paths, cancellationToken);

    /// <summary>Moves staged files back to Unstaged. Their changes stay in the working tree.</summary>
    public async Task UnstageAsync(string repositoryPath, IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default)
    {
        if (await HasCommitsAsync(repositoryPath, cancellationToken))
        {
            await RunInBatchesAsync(repositoryPath, ["restore", "--staged"], paths, cancellationToken);
        }
        else
        {
            // Before the first commit there's no HEAD to restore from: just remove them from the index.
            await RunInBatchesAsync(repositoryPath, ["rm", "--cached", "-r", "-q"], paths, cancellationToken);
        }
    }

    /// <summary>
    /// Throws away unstaged changes. Tracked files go back to their staged (or committed) content.
    /// Untracked files are moved to the Recycle Bin, so a mistake can still be undone there.
    /// </summary>
    public async Task DiscardAsync(string repositoryPath, IReadOnlyCollection<FileChange> changes, CancellationToken cancellationToken = default)
    {
        var tracked = changes.Where(change => change.Kind != ChangeKind.Untracked).Select(change => change.Path).ToList();
        var untracked = changes.Where(change => change.Kind == ChangeKind.Untracked).Select(change => change.Path).ToList();

        if (tracked.Count > 0)
        {
            await RunInBatchesAsync(repositoryPath, ["restore", "--worktree"], tracked, cancellationToken);
        }

        foreach (var path in untracked)
        {
            var fullPath = Path.Combine(repositoryPath, path.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(fullPath))
            {
                _deleteUntrackedFile(fullPath);
            }
        }
    }

    /// <summary>
    /// A clean slate: every uncommitted change goes, staged or not, hidden or not. New files (staged ones too)
    /// go to the Recycle Bin; tracked files go back to the last commit. Ignored files, commits and stashes stay.
    /// </summary>
    public async Task DiscardAllAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var hasCommits = await HasCommitsAsync(repositoryPath, cancellationToken);

        // Unstage everything first, so new staged files count as new files (and go to the Recycle Bin, not away for good).
        string[] unstage = hasCommits ? ["reset", "--quiet"] : ["rm", "-r", "--cached", "--quiet", "--ignore-unmatch", "."];
        await _git.RunCheckedAsync(repositoryPath, unstage, cancellationToken: cancellationToken);

        var untracked = await _git.RunCheckedAsync(repositoryPath, ["ls-files", "--others", "--exclude-standard", "-z"], cancellationToken: cancellationToken);
        foreach (var path in untracked.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var fullPath = Path.Combine(repositoryPath, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                _deleteUntrackedFile(fullPath);
            }
        }

        if (hasCommits)
        {
            await _git.RunCheckedAsync(repositoryPath, ["reset", "--hard", "--quiet", "HEAD"], cancellationToken: cancellationToken);
        }
    }

    public async Task<bool> HasCommitsAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-parse", "--verify", "-q", "HEAD"], cancellationToken: cancellationToken);
        return result.Succeeded;
    }

    private async Task RunInBatchesAsync(
        string repositoryPath,
        IReadOnlyList<string> command,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        foreach (var batch in paths.Chunk(PathsPerCommand))
        {
            // "--" ends the options, so a file named like "-f" is still treated as a path.
            await _git.RunCheckedAsync(repositoryPath, [.. command, "--", .. batch], cancellationToken: cancellationToken);
        }
    }
}

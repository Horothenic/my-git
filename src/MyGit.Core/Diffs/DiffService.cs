using System.Text;
using MyGit.Core.Changes;
using MyGit.Core.Git;

namespace MyGit.Core.Diffs;

/// <summary>
/// Gets the diff of one file: its unstaged changes, or its staged ones.
/// </summary>
public sealed class DiffService
{
    /// <summary>Files bigger than this need an explicit "Load diff anyway".</summary>
    public const long LargeFileBytes = 2 * 1024 * 1024;

    private readonly IGitRunner _git;

    public DiffService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>True when the working copy of the file is big enough that the diff should wait for a click.</summary>
    public static bool IsLarge(string repositoryPath, string path)
    {
        var file = new FileInfo(FullPath(repositoryPath, path));
        return file.Exists && file.Length > LargeFileBytes;
    }

    /// <param name="staged">True for the Staged list (index vs HEAD), false for Unstaged (working tree vs index).</param>
    public async Task<FileDiff> GetDiffAsync(string repositoryPath, FileChange change, bool staged, CancellationToken cancellationToken = default)
    {
        if (!staged && change.Kind == ChangeKind.Untracked)
        {
            return await ReadNewFileAsync(repositoryPath, change.Path, cancellationToken);
        }

        List<string> arguments = ["--no-optional-locks", "diff", "--no-color", "--no-ext-diff", "-U3"];
        if (staged)
        {
            arguments.Add("--cached");
            arguments.Add("-M"); // show renames as renames
        }

        arguments.Add("--");
        if (change.OriginalPath is not null)
        {
            arguments.Add(change.OriginalPath);
        }

        arguments.Add(change.Path);

        var result = await _git.RunCheckedAsync(repositoryPath, arguments, cancellationToken: cancellationToken);
        return DiffParser.Parse(change.Path, result.StandardOutput);
    }

    private static async Task<FileDiff> ReadNewFileAsync(string repositoryPath, string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(FullPath(repositoryPath, path), cancellationToken);

        // Same rule git uses: a NUL byte in the first 8 KB means binary.
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0))
        {
            return FileDiff.Binary(path);
        }

        return DiffParser.ForNewFile(path, Encoding.UTF8.GetString(bytes));
    }

    private static string FullPath(string repositoryPath, string path) =>
        Path.Combine(repositoryPath, path.Replace('/', Path.DirectorySeparatorChar));
}

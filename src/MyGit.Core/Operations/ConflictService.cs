using MyGit.Core.Git;

namespace MyGit.Core.Operations;

/// <summary>Which side of a conflict to keep for a whole file.</summary>
public enum ConflictSide
{
    /// <summary>git's "ours": your branch in a merge, the branch you're rebasing onto in a rebase.</summary>
    Ours,

    /// <summary>git's "theirs": the branch being merged in, or your commit being replayed in a rebase.</summary>
    Theirs,
}

/// <summary>
/// Resolves conflicted files as a whole: keep one side, or mark a file you fixed yourself as resolved.
/// </summary>
public sealed class ConflictService
{
    private readonly IGitRunner _git;

    public ConflictService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Keeps one side's version of the whole file and marks it resolved. When that side deleted
    /// the file, the file is deleted.
    /// </summary>
    public async Task UseSideAsync(string repositoryPath, string path, ConflictSide side, CancellationToken cancellationToken = default)
    {
        // git stores the conflicting versions as stages: 1 = common base, 2 = ours, 3 = theirs.
        var stage = side == ConflictSide.Ours ? "2" : "3";
        var stages = await _git.RunCheckedAsync(repositoryPath, ["ls-files", "-u", "--", path], cancellationToken: cancellationToken);
        var sideHasFile = stages.OutputLines.Any(line => line.Split('\t')[0].Split(' ').ElementAtOrDefault(2) == stage);

        if (sideHasFile)
        {
            await _git.RunCheckedAsync(repositoryPath, ["checkout", side == ConflictSide.Ours ? "--ours" : "--theirs", "--", path], cancellationToken: cancellationToken);
            await _git.RunCheckedAsync(repositoryPath, ["add", "--", path], cancellationToken: cancellationToken);
        }
        else
        {
            await _git.RunCheckedAsync(repositoryPath, ["rm", "--quiet", "--", path], cancellationToken: cancellationToken);
        }
    }

    /// <summary>Marks a file you fixed (in an editor or merge tool) as resolved. A deleted file stays deleted.</summary>
    public Task MarkResolvedAsync(string repositoryPath, string path, CancellationToken cancellationToken = default)
    {
        var exists = File.Exists(Path.Combine(repositoryPath, path.Replace('/', Path.DirectorySeparatorChar)));
        return exists
            ? _git.RunCheckedAsync(repositoryPath, ["add", "--", path], cancellationToken: cancellationToken)
            : _git.RunCheckedAsync(repositoryPath, ["rm", "--quiet", "--", path], cancellationToken: cancellationToken);
    }

    /// <summary>True when the file still contains conflict markers (&lt;&lt;&lt;&lt;&lt;&lt;&lt; … &gt;&gt;&gt;&gt;&gt;&gt;&gt;).</summary>
    public static bool HasConflictMarkers(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        foreach (var line in File.ReadLines(fullPath))
        {
            if (line.StartsWith("<<<<<<< ", StringComparison.Ordinal) || line == "<<<<<<<")
            {
                return true;
            }
        }

        return false;
    }
}

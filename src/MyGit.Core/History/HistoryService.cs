using System.Globalization;
using MyGit.Core.Changes;
using MyGit.Core.Diffs;
using MyGit.Core.Git;

namespace MyGit.Core.History;

/// <summary>
/// Reads the commit history and creates branches and tags from commits.
/// </summary>
public sealed class HistoryService
{
    /// <summary>git's id for an empty tree, used to diff the very first commit against "nothing".</summary>
    private const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    // Fields are separated by NUL (%x00) and commits by a record separator (%x1e), which can't appear in messages.
    private const string LogFormat = "%H%x00%P%x00%an%x00%at%x00%s%x00%D%x1e";

    private readonly IGitRunner _git;

    public HistoryService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Commits reachable from where you are (HEAD) and from its upstream (so commits to pull show too),
    /// newest first in topological order. Use <paramref name="skip"/> to page through long histories.
    /// </summary>
    public async Task<IReadOnlyList<CommitSummary>> GetCommitsAsync(string repositoryPath, int skip, int count, CancellationToken cancellationToken = default)
    {
        var heads = new List<string> { "HEAD" };

        var upstream = await _git.RunAsync(repositoryPath, ["rev-parse", "--verify", "-q", "@{u}"], cancellationToken: cancellationToken);
        if (upstream.Succeeded)
        {
            heads.Add("@{u}");
        }

        var result = await _git.RunAsync(
            repositoryPath,
            ["--no-optional-locks", "log", "--topo-order", $"--format={LogFormat}", $"--skip={skip}", $"--max-count={count}", .. heads, "--"],
            cancellationToken: cancellationToken);

        // A repository without commits has no history yet.
        return result.Succeeded ? ParseLog(result.StandardOutput) : [];
    }

    public static IReadOnlyList<CommitSummary> ParseLog(string output)
    {
        var commits = new List<CommitSummary>();

        foreach (var record in output.Split('\x1e'))
        {
            var text = record.TrimStart('\r', '\n');
            if (text.Length == 0)
            {
                continue;
            }

            var fields = text.Split('\0');
            if (fields.Length < 6)
            {
                continue;
            }

            commits.Add(new CommitSummary(
                fields[0],
                fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                fields[2],
                DateTimeOffset.FromUnixTimeSeconds(long.Parse(fields[3], CultureInfo.InvariantCulture)).ToLocalTime(),
                fields[4],
                ParseRefs(fields[5])));
        }

        return commits;
    }

    /// <summary>
    /// Parses git's "%D" decoration, e.g. "HEAD -> main, origin/main, origin/HEAD, tag: v1.2.0".
    /// </summary>
    public static IReadOnlyList<RefLabel> ParseRefs(string decoration)
    {
        var refs = new List<RefLabel>();

        foreach (var raw in decoration.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.StartsWith("HEAD -> ", StringComparison.Ordinal))
            {
                refs.Add(new RefLabel(RefKind.Head, "HEAD"));
                refs.Add(new RefLabel(RefKind.LocalBranch, raw["HEAD -> ".Length..]));
            }
            else if (raw == "HEAD")
            {
                refs.Add(new RefLabel(RefKind.Head, "HEAD")); // detached
            }
            else if (raw.StartsWith("tag: ", StringComparison.Ordinal))
            {
                refs.Add(new RefLabel(RefKind.Tag, raw["tag: ".Length..]));
            }
            else if (raw.EndsWith("/HEAD", StringComparison.Ordinal))
            {
                // "origin/HEAD" just points at the remote's default branch: noise in the list.
            }
            else if (raw.Contains('/') && !raw.StartsWith("refs/", StringComparison.Ordinal))
            {
                // Ambiguous by name alone ("feature/x" vs "origin/x"); fixed up by ClassifyAsync.
                refs.Add(new RefLabel(RefKind.RemoteBranch, raw));
            }
            else
            {
                refs.Add(new RefLabel(RefKind.LocalBranch, raw));
            }
        }

        return refs;
    }

    /// <summary>
    /// Branch names with a slash ("feature/inventory") look like remote ones ("origin/main").
    /// This corrects the labels using the repository's real list of local branches.
    /// </summary>
    public async Task<IReadOnlyList<CommitSummary>> ClassifyRefsAsync(string repositoryPath, IReadOnlyList<CommitSummary> commits, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["for-each-ref", "--format=%(refname:short)", "refs/heads"], cancellationToken: cancellationToken);
        var local = result.OutputLines.ToHashSet(StringComparer.Ordinal);

        return commits.Select(commit => commit with
        {
            Refs = commit.Refs.Select(label =>
                label.Kind == RefKind.RemoteBranch && local.Contains(label.Name) ? label with { Kind = RefKind.LocalBranch } : label).ToList(),
        }).ToList();
    }

    public async Task<CommitDetails> GetDetailsAsync(string repositoryPath, string sha, CancellationToken cancellationToken = default)
    {
        var info = await _git.RunCheckedAsync(
            repositoryPath,
            ["show", "-s", "--format=%H%x00%P%x00%an%x00%ae%x00%at%x00%B", sha],
            cancellationToken: cancellationToken);
        var fields = info.StandardOutput.Split('\0', 6);
        var parents = fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Files changed compared to the first parent (or to nothing, for the first commit).
        var from = parents.Length > 0 ? parents[0] : EmptyTree;
        var files = await _git.RunCheckedAsync(
            repositoryPath,
            ["diff", "--name-status", "-z", "-M", from, sha],
            cancellationToken: cancellationToken);

        return new CommitDetails(
            fields[0],
            parents,
            fields[2],
            fields[3],
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(fields[4], CultureInfo.InvariantCulture)).ToLocalTime(),
            fields[5].Trim(),
            ParseNameStatus(files.StandardOutput));
    }

    /// <summary>Parses "git diff --name-status -z": "M\0path\0", "R100\0old\0new\0", ...</summary>
    public static IReadOnlyList<FileChange> ParseNameStatus(string output)
    {
        var parts = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = new List<FileChange>();

        for (var i = 0; i < parts.Length; i++)
        {
            var status = parts[i][0];
            var kind = status switch
            {
                'A' => ChangeKind.Added,
                'D' => ChangeKind.Deleted,
                'R' => ChangeKind.Renamed,
                'C' => ChangeKind.Copied,
                'T' => ChangeKind.TypeChanged,
                _ => ChangeKind.Modified,
            };

            if (kind is ChangeKind.Renamed or ChangeKind.Copied && i + 2 < parts.Length)
            {
                files.Add(new FileChange(parts[i + 2], kind, parts[i + 1]));
                i += 2;
            }
            else if (i + 1 < parts.Length)
            {
                files.Add(new FileChange(parts[i + 1], kind));
                i += 1;
            }
        }

        return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The diff of one file in a commit (against its first parent).</summary>
    public async Task<FileDiff> GetFileDiffAsync(string repositoryPath, CommitDetails commit, FileChange file, CancellationToken cancellationToken = default)
    {
        var from = commit.Parents.Count > 0 ? commit.Parents[0] : EmptyTree;
        List<string> arguments = ["diff", "--no-color", "--no-ext-diff", "-U3", "-M", from, commit.Sha, "--"];

        if (file.OriginalPath is not null)
        {
            arguments.Add(file.OriginalPath);
        }

        arguments.Add(file.Path);

        var result = await _git.RunCheckedAsync(repositoryPath, arguments, cancellationToken: cancellationToken);
        return DiffParser.Parse(file.Path, result.StandardOutput);
    }

    /// <summary>Returns why <paramref name="name"/> can't be a branch name, or null if it can.</summary>
    public async Task<string?> ValidateBranchNameAsync(string repositoryPath, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a name.";
        }

        var format = await _git.RunAsync(repositoryPath, ["check-ref-format", "--branch", name], cancellationToken: cancellationToken);
        if (!format.Succeeded)
        {
            return "That isn't a valid branch name (no spaces, and no characters like ~ ^ : ? * [ \\).";
        }

        var exists = await _git.RunAsync(repositoryPath, ["show-ref", "--verify", "-q", $"refs/heads/{name}"], cancellationToken: cancellationToken);
        return exists.Succeeded ? $"A branch called {name} already exists." : null;
    }

    /// <summary>Returns why <paramref name="name"/> can't be a tag name, or null if it can.</summary>
    public async Task<string?> ValidateTagNameAsync(string repositoryPath, string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a name.";
        }

        var format = await _git.RunAsync(repositoryPath, ["check-ref-format", $"refs/tags/{name}"], cancellationToken: cancellationToken);
        if (!format.Succeeded)
        {
            return "That isn't a valid tag name (no spaces, and no characters like ~ ^ : ? * [ \\).";
        }

        var exists = await _git.RunAsync(repositoryPath, ["show-ref", "--verify", "-q", $"refs/tags/{name}"], cancellationToken: cancellationToken);
        return exists.Succeeded ? $"A tag called {name} already exists." : null;
    }

    /// <summary>Creates a branch at <paramref name="startPoint"/> (a commit id or tag), and switches to it if asked.</summary>
    public Task CreateBranchAsync(string repositoryPath, string name, string startPoint, bool switchToIt, CancellationToken cancellationToken = default) =>
        switchToIt
            ? _git.RunCheckedAsync(repositoryPath, ["switch", "-c", name, startPoint], cancellationToken: cancellationToken)
            : _git.RunCheckedAsync(repositoryPath, ["branch", name, startPoint], cancellationToken: cancellationToken);

    /// <summary>
    /// Creates a tag on a commit. With a message it's an annotated tag (author and date recorded),
    /// without one a lightweight tag. With <paramref name="push"/> it's also sent to origin.
    /// </summary>
    public async Task CreateTagAsync(string repositoryPath, string name, string sha, string? message, bool push, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            await _git.RunCheckedAsync(repositoryPath, ["tag", name, sha], cancellationToken: cancellationToken);
        }
        else
        {
            await _git.RunCheckedAsync(repositoryPath, ["tag", "-a", name, "-m", message.Trim(), sha], cancellationToken: cancellationToken);
        }

        if (push)
        {
            await _git.RunCheckedAsync(repositoryPath, ["push", "origin", $"refs/tags/{name}"], cancellationToken: cancellationToken);
        }
    }

    /// <summary>Checks out a commit without a branch (detached HEAD), e.g. to look at an old version.</summary>
    public Task CheckoutDetachedAsync(string repositoryPath, string sha, CancellationToken cancellationToken = default) =>
        _git.RunCheckedAsync(repositoryPath, ["switch", "--detach", sha], cancellationToken: cancellationToken);
}

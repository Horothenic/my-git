using System.Globalization;
using System.Text.RegularExpressions;
using MyGit.Core.Git;

namespace MyGit.Core.Branches;

/// <summary>
/// Lists, switches, renames, deletes and fast-forwards branches.
/// </summary>
public sealed partial class BranchService
{
    private readonly IGitRunner _git;

    public BranchService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Local branches (current first, then by name) and remote branches nobody tracks here yet.
    /// Ahead/behind counts come from the last fetch.
    /// </summary>
    public async Task<BranchList> ListAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var local = await _git.RunCheckedAsync(
            repositoryPath,
            ["for-each-ref", "--format=%(refname:short)%00%(upstream:short)%00%(upstream:track,nobracket)%00%(HEAD)", "refs/heads"],
            cancellationToken: cancellationToken);

        var branches = new List<LocalBranch>();
        foreach (var line in local.OutputLines)
        {
            var fields = line.Split('\0');
            if (fields.Length < 4)
            {
                continue;
            }

            var upstream = fields[1].Length == 0 ? null : fields[1];
            var (ahead, behind, gone) = ParseTrack(fields[2]);
            branches.Add(new LocalBranch(fields[0], upstream, ahead, behind, fields[3] == "*", gone));
        }

        var tracked = branches.Where(b => b.Upstream is not null).Select(b => b.Upstream!).ToHashSet(StringComparer.Ordinal);
        var localNames = branches.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);

        var remote = await _git.RunCheckedAsync(
            repositoryPath,
            ["for-each-ref", "--format=%(refname:short)%00%(refname:lstrip=2)", "refs/remotes"],
            cancellationToken: cancellationToken);

        var remotes = new List<RemoteBranch>();
        foreach (var line in remote.OutputLines)
        {
            var fields = line.Split('\0');
            var name = fields[0];
            var slash = name.IndexOf('/', StringComparison.Ordinal);

            // Skip "origin/HEAD" (a pointer, not a branch), and branches already tracked or checked out here.
            if (slash < 0 || name.EndsWith("/HEAD", StringComparison.Ordinal) || tracked.Contains(name))
            {
                continue;
            }

            var localName = name[(slash + 1)..];
            if (!localNames.Contains(localName))
            {
                remotes.Add(new RemoteBranch(name, name[..slash], localName));
            }
        }

        return new BranchList(
            branches.OrderByDescending(b => b.IsCurrent).ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            remotes.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>
    /// Switches to a local branch. If uncommitted changes would be overwritten, nothing happens and
    /// <see cref="SwitchOutcome.NeedsStash"/> is returned; call again with <paramref name="stash"/>
    /// to stash them, switch, and bring them back.
    /// </summary>
    public Task<SwitchOutcome> SwitchAsync(string repositoryPath, string branch, bool stash = false, CancellationToken cancellationToken = default) =>
        RunSwitchAsync(repositoryPath, ["switch", branch], branch, stash, cancellationToken);

    /// <summary>Checks out a remote branch as a new local branch that tracks it (e.g. origin/fix-login → fix-login).</summary>
    public Task<SwitchOutcome> CheckoutRemoteAsync(string repositoryPath, RemoteBranch branch, bool stash = false, CancellationToken cancellationToken = default) =>
        RunSwitchAsync(repositoryPath, ["switch", "--create", branch.LocalName, "--track", branch.Name], branch.LocalName, stash, cancellationToken);

    /// <summary>Renames a local branch. Its link to GitHub (if any) stays pointed at the old remote branch.</summary>
    public async Task RenameAsync(string repositoryPath, string oldName, string newName, CancellationToken cancellationToken = default)
    {
        await _git.RunCheckedAsync(repositoryPath, ["branch", "--move", oldName, newName], cancellationToken: cancellationToken);

        // A branch stacked on the renamed one keeps pointing at it (git moves the renamed branch's own settings).
        var children = await _git.RunAsync(repositoryPath, ["config", "--local", "--get-regexp", @"^branch\..*\.mygit-parent$"], cancellationToken: cancellationToken);
        foreach (var line in children.Succeeded ? children.OutputLines : [])
        {
            var space = line.IndexOf(' ');
            if (space > 0 && line[(space + 1)..].Trim() == oldName)
            {
                await _git.RunCheckedAsync(repositoryPath, ["config", line[..space], newName], cancellationToken: cancellationToken);
            }
        }
    }

    /// <summary>
    /// Deletes a local branch (never the one you're on). Without <paramref name="force"/>, refuses
    /// when the branch has commits that aren't merged anywhere.
    /// </summary>
    public async Task<DeleteOutcome> DeleteAsync(string repositoryPath, string branch, bool force = false, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["branch", force ? "-D" : "-d", branch], cancellationToken: cancellationToken);

        if (result.Succeeded)
        {
            return DeleteOutcome.Done;
        }

        if (result.StandardError.Contains("not fully merged", StringComparison.OrdinalIgnoreCase))
        {
            return DeleteOutcome.NotMerged;
        }

        throw new GitCommandException(["branch", "-d", branch], result);
    }

    /// <summary>
    /// Brings a branch you're not on up to date with its GitHub copy, without switching to it.
    /// Only moves forward: if the branch has its own commits, nothing changes.
    /// </summary>
    public async Task<FastForwardOutcome> FastForwardAsync(string repositoryPath, LocalBranch branch, CancellationToken cancellationToken = default)
    {
        if (branch.IsCurrent || branch.Upstream is null)
        {
            throw new InvalidOperationException("Only a branch you're not on, that tracks a remote branch, can be fast-forwarded like this.");
        }

        var target = await _git.RunCheckedAsync(
            repositoryPath,
            ["for-each-ref", "--format=%(upstream:remotename)%00%(upstream:remoteref)", $"refs/heads/{branch.Name}"],
            cancellationToken: cancellationToken);
        var fields = target.StandardOutput.Trim().Split('\0');

        // "git fetch origin main:main" updates refs/heads/main only if that's a fast-forward.
        var result = await _git.RunAsync(
            repositoryPath,
            ["fetch", fields[0], $"{fields[1]}:refs/heads/{branch.Name}"],
            cancellationToken: cancellationToken);

        if (result.Succeeded)
        {
            return FastForwardOutcome.Done;
        }

        if (result.StandardError.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
            result.StandardError.Contains("[rejected]", StringComparison.Ordinal))
        {
            return FastForwardOutcome.Diverged;
        }

        throw new GitCommandException(["fetch", fields[0], $"{fields[1]}:refs/heads/{branch.Name}"], result);
    }

    /// <summary>Parses %(upstream:track,nobracket): "ahead 1, behind 4", "behind 2", "gone", or "".</summary>
    public static (int Ahead, int Behind, bool Gone) ParseTrack(string track)
    {
        if (track.Trim() == "gone")
        {
            return (0, 0, true);
        }

        var ahead = TrackAhead().Match(track);
        var behind = TrackBehind().Match(track);

        return (
            ahead.Success ? int.Parse(ahead.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
            behind.Success ? int.Parse(behind.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
            false);
    }

    private async Task<SwitchOutcome> RunSwitchAsync(string repositoryPath, IReadOnlyList<string> switchArguments, string branch, bool stash, CancellationToken cancellationToken)
    {
        var stashed = false;

        if (stash)
        {
            var push = await _git.RunCheckedAsync(
                repositoryPath,
                ["stash", "push", "--include-untracked", "--message", $"My Git: switching to {branch}"],
                cancellationToken: cancellationToken);
            stashed = !push.StandardOutput.Contains("No local changes", StringComparison.OrdinalIgnoreCase);
        }

        var result = await _git.RunAsync(repositoryPath, switchArguments, cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            if (stashed)
            {
                // The switch failed for another reason: put the changes back where they were.
                await _git.RunAsync(repositoryPath, ["stash", "pop"], cancellationToken: cancellationToken);
            }

            if (result.StandardError.Contains("would be overwritten", StringComparison.OrdinalIgnoreCase))
            {
                return SwitchOutcome.NeedsStash;
            }

            throw new GitCommandException(switchArguments, result);
        }

        if (stashed)
        {
            var pop = await _git.RunAsync(repositoryPath, ["stash", "pop"], cancellationToken: cancellationToken);
            if (!pop.Succeeded)
            {
                // The changes clash with this branch. git leaves them half-applied with conflicts;
                // undo that so the branch is clean. Nothing is lost: a failed pop keeps the stash, and
                // everything uncommitted went into it before the switch.
                await _git.RunAsync(repositoryPath, ["reset", "--hard", "--quiet"], cancellationToken: cancellationToken);
                await _git.RunAsync(repositoryPath, ["clean", "-fd", "--quiet"], cancellationToken: cancellationToken);
                return SwitchOutcome.StashConflict;
            }
        }

        return SwitchOutcome.Done;
    }

    [GeneratedRegex(@"ahead (\d+)")]
    private static partial Regex TrackAhead();

    [GeneratedRegex(@"behind (\d+)")]
    private static partial Regex TrackBehind();
}

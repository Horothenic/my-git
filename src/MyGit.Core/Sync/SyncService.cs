using System.Globalization;
using MyGit.Core.Git;

namespace MyGit.Core.Sync;

/// <summary>
/// Fetch, pull and push for the current branch.
/// Authentication comes from <see cref="GitRunner"/>'s environment (the signed-in GitHub account).
/// </summary>
public sealed class SyncService
{
    private readonly IGitRunner _git;

    public SyncService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Ahead/behind counts from the last fetch. Fast and offline: it only reads local refs.
    /// </summary>
    public async Task<TrackingInfo> GetTrackingAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var branchResult = await _git.RunAsync(repositoryPath, ["branch", "--show-current"], cancellationToken: cancellationToken);
        var branch = branchResult.StandardOutput.Trim();
        var remotes = await _git.RunAsync(repositoryPath, ["remote"], cancellationToken: cancellationToken);
        var hasRemote = remotes.OutputLines.Count > 0;

        if (branch.Length == 0)
        {
            return new TrackingInfo(null, null, 0, 0, hasRemote);
        }

        var upstream = await GetUpstreamAsync(repositoryPath, cancellationToken);
        if (upstream is null)
        {
            return new TrackingInfo(branch, null, 0, 0, hasRemote);
        }

        // "<ahead>\t<behind>" for HEAD...upstream
        var counts = await _git.RunAsync(repositoryPath, ["rev-list", "--left-right", "--count", "HEAD...@{u}"], cancellationToken: cancellationToken);
        var parts = counts.StandardOutput.Split('\t', StringSplitOptions.TrimEntries);
        var ahead = counts.Succeeded && parts.Length == 2 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
        var behind = counts.Succeeded && parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;

        return new TrackingInfo(branch, upstream, ahead, behind, hasRemote);
    }

    /// <summary>Downloads new commits and branches from every remote (nothing in your files changes).</summary>
    public Task FetchAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        _git.RunCheckedAsync(repositoryPath, ["fetch", "--all", "--prune"], cancellationToken: cancellationToken);

    /// <summary>
    /// Pulls the upstream into the current branch. Uncommitted changes are stashed and put back
    /// automatically (--autostash).
    /// </summary>
    public async Task<PullResult> PullAsync(string repositoryPath, PullMode mode, CancellationToken cancellationToken = default)
    {
        var upstream = await GetUpstreamAsync(repositoryPath, cancellationToken);
        if (upstream is null)
        {
            return new PullResult(PullOutcome.NoUpstream, 0, string.Empty);
        }

        var before = await HeadAsync(repositoryPath, cancellationToken);

        string[] modeArguments = mode switch
        {
            PullMode.Merge => ["--no-rebase"],
            PullMode.FastForwardOnly => ["--ff-only"],
            _ => ["--rebase"],
        };

        var result = await _git.RunAsync(repositoryPath, ["pull", .. modeArguments, "--autostash"], cancellationToken: cancellationToken);
        var output = result.StandardOutput + result.StandardError;
        var pulled = await CountAsync(repositoryPath, [$"{before}..@{{u}}"], cancellationToken);

        if (result.Succeeded)
        {
            // git prints this and still exits 0 when the stash couldn't go back cleanly.
            return output.Contains("autostash", StringComparison.OrdinalIgnoreCase) && output.Contains("conflict", StringComparison.OrdinalIgnoreCase)
                ? new PullResult(PullOutcome.StashConflict, pulled, upstream, output.Trim())
                : new PullResult(PullOutcome.Done, pulled, upstream);
        }

        if (output.Contains("Not possible to fast-forward", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("Diverging branches can't be fast-forwarded", StringComparison.OrdinalIgnoreCase))
        {
            return new PullResult(PullOutcome.NotFastForward, 0, upstream, output.Trim());
        }

        if (output.Contains("CONFLICT", StringComparison.Ordinal) || await IsRebaseOrMergeInProgressAsync(repositoryPath, cancellationToken))
        {
            return new PullResult(PullOutcome.Conflict, pulled, upstream, output.Trim());
        }

        throw new GitCommandException(["pull", .. modeArguments, "--autostash"], result);
    }

    /// <summary>
    /// Pushes the current branch. A branch that isn't on the remote yet is published
    /// (pushed and set to track it). <paramref name="force"/> uses --force-with-lease, which still
    /// refuses if the remote has commits you haven't fetched.
    /// </summary>
    public async Task<PushResult> PushAsync(string repositoryPath, bool force = false, CancellationToken cancellationToken = default)
    {
        var tracking = await GetTrackingAsync(repositoryPath, cancellationToken);
        if (tracking.IsDetached)
        {
            throw new InvalidOperationException("You're not on a branch (detached HEAD), so there's nothing to push to.");
        }

        List<string> arguments = ["push"];
        var publishing = tracking.Upstream is null;

        // How many commits go up: for a new branch, those not on any remote branch yet
        // (counted now, because after the push the new remote branch contains them).
        var pushing = publishing
            ? await CountAsync(repositoryPath, ["HEAD", "--not", "--remotes"], cancellationToken)
            : tracking.Ahead;

        if (publishing)
        {
            // Publish: create the branch on origin with the same name and track it.
            arguments.AddRange(["--set-upstream", "origin", "HEAD"]);
        }

        if (force)
        {
            arguments.Add("--force-with-lease");
        }

        var result = await _git.RunAsync(repositoryPath, arguments, cancellationToken: cancellationToken);
        var output = result.StandardError + result.StandardOutput;

        if (!result.Succeeded)
        {
            if (output.Contains("stale info", StringComparison.OrdinalIgnoreCase))
            {
                return new PushResult(PushOutcome.LeaseRejected, 0, tracking.Upstream ?? string.Empty);
            }

            if (output.Contains("[rejected]", StringComparison.Ordinal) || output.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("fetch first", StringComparison.OrdinalIgnoreCase))
            {
                return new PushResult(PushOutcome.Rejected, 0, tracking.Upstream ?? string.Empty);
            }

            throw new GitCommandException(arguments, result);
        }

        var upstream = await GetUpstreamAsync(repositoryPath, cancellationToken) ?? $"origin/{tracking.Branch}";
        return new PushResult(PushOutcome.Done, pushing, upstream, publishing);
    }

    private async Task<string?> GetUpstreamAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"], cancellationToken: cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    private async Task<string> HeadAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-parse", "HEAD"], cancellationToken: cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : "HEAD";
    }

    private async Task<int> CountAsync(string repositoryPath, IReadOnlyList<string> revisions, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-list", "--count", .. revisions], cancellationToken: cancellationToken);
        return result.Succeeded && int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    private async Task<bool> IsRebaseOrMergeInProgressAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var gitDir = await _git.RunAsync(repositoryPath, ["rev-parse", "--absolute-git-dir"], cancellationToken: cancellationToken);
        var path = gitDir.StandardOutput.Trim();

        return Directory.Exists(Path.Combine(path, "rebase-merge"))
            || Directory.Exists(Path.Combine(path, "rebase-apply"))
            || File.Exists(Path.Combine(path, "MERGE_HEAD"));
    }
}

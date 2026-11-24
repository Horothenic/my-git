using MyGit.Core.Git;
using MyGit.Core.Operations;

namespace MyGit.Core.Stacks;

/// <summary>One branch in a stack, with how it stands against the branch below it.</summary>
/// <param name="Parent">The branch below (the trunk, e.g. "main", for the bottom branch).</param>
/// <param name="Base">Where this branch started: the parent's tip when it was last stacked on it.</param>
/// <param name="CommitCount">Its own commits (the ones its parent doesn't have).</param>
/// <param name="NeedsRestack">The parent changed (amended, or new commits) since this branch was built on it.</param>
public sealed record StackEntry(string Name, string Parent, string? Base, int CommitCount, bool NeedsRestack);

/// <summary>A chain of branches, each built on the one before it, bottom first.</summary>
public sealed record Stack(string Trunk, IReadOnlyList<StackEntry> Branches)
{
    /// <summary>Named after its bottom branch.</summary>
    public string Name => Branches[0].Name;

    public StackEntry Top => Branches[^1];

    public bool NeedsRestack => Branches.Any(branch => branch.NeedsRestack);

    public bool Contains(string branch) => Branches.Any(entry => entry.Name == branch);
}

/// <summary>
/// Stacked branches. Each branch remembers its parent and where it started in the repository's
/// local git config (branch.&lt;name&gt;.mygit-parent / mygit-base), so nothing is pushed or shared.
/// </summary>
public sealed class StackService
{
    private const string ParentKey = "mygit-parent";
    private const string BaseKey = "mygit-base";

    private readonly IGitRunner _git;
    private readonly RebaseService _rebase;

    public StackService(IGitRunner git, RebaseService rebase)
    {
        _git = git;
        _rebase = rebase;
    }

    /// <summary>
    /// Every stack in the repository. A branch only counts while it and its parent exist.
    /// Bases are brought up to date along the way: when a branch already sits on its parent's tip, that tip is its base.
    /// </summary>
    public async Task<IReadOnlyList<Stack>> GetStacksAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var parents = await ReadConfigAsync(repositoryPath, ParentKey, cancellationToken);
        var bases = await ReadConfigAsync(repositoryPath, BaseKey, cancellationToken);
        var existing = (await _git.RunCheckedAsync(repositoryPath, ["for-each-ref", "--format=%(refname:short)", "refs/heads"], cancellationToken: cancellationToken))
            .OutputLines.ToHashSet(StringComparer.Ordinal);

        var tracked = parents.Where(pair => existing.Contains(pair.Key) && existing.Contains(pair.Value)).ToDictionary();
        var children = tracked.GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).Order().ToList());

        var stacks = new List<Stack>();

        // Bottoms: tracked branches whose parent isn't tracked itself (the trunk).
        foreach (var bottom in tracked.Keys.Where(branch => !tracked.ContainsKey(tracked[branch])).Order())
        {
            var entries = new List<StackEntry>();
            var current = bottom;
            while (current is not null)
            {
                entries.Add(await DescribeAsync(repositoryPath, current, tracked[current], bases.GetValueOrDefault(current), cancellationToken));
                current = children.GetValueOrDefault(current)?.FirstOrDefault();
            }

            stacks.Add(new Stack(tracked[bottom], entries));
        }

        return stacks;
    }

    /// <summary>The branch stacks start from: GitHub's default branch (origin/HEAD), else main or master.</summary>
    public async Task<string> GetTrunkAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var head = await _git.RunAsync(repositoryPath, ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], cancellationToken: cancellationToken);
        if (head.Succeeded && head.StandardOutput.Trim() is { Length: > 0 } remoteHead)
        {
            return remoteHead[(remoteHead.IndexOf('/') + 1)..];
        }

        return await TryTipAsync(repositoryPath, "main", cancellationToken) is not null ? "main" : "master";
    }

    /// <summary>
    /// Creates <paramref name="name"/> on top of the checked-out branch and switches to it. A branch that isn't
    /// in a stack yet becomes the bottom of a new one (on <paramref name="trunk"/>); on the trunk itself, the new
    /// branch starts a stack. Throws when the checked-out branch already has a branch stacked on it.
    /// </summary>
    public async Task CreateOnTopAsync(string repositoryPath, string name, string trunk, CancellationToken cancellationToken = default)
    {
        var current = (await _git.RunCheckedAsync(repositoryPath, ["branch", "--show-current"], cancellationToken: cancellationToken)).StandardOutput.Trim();
        if (current.Length == 0)
        {
            throw new InvalidOperationException("Switch to a branch first: you're not on one.");
        }

        var parents = await ReadConfigAsync(repositoryPath, ParentKey, cancellationToken);
        if (parents.FirstOrDefault(pair => pair.Value == current).Key is { } child)
        {
            throw new InvalidOperationException($"{child} is already stacked on {current}. Switch to the top of the stack to add a branch there.");
        }

        if (current != trunk && !parents.ContainsKey(current))
        {
            var forkPoint = (await _git.RunCheckedAsync(repositoryPath, ["merge-base", trunk, current], cancellationToken: cancellationToken)).StandardOutput.Trim();
            await SetAsync(repositoryPath, current, trunk, forkPoint, cancellationToken);
        }

        var tip = await TipAsync(repositoryPath, current, cancellationToken);
        await _git.RunCheckedAsync(repositoryPath, ["switch", "--quiet", "--create", name], cancellationToken: cancellationToken);
        await SetAsync(repositoryPath, name, current, tip, cancellationToken);
    }

    /// <summary>Takes a branch out of its stack (the branch itself stays). Branches above it then sit on its parent.</summary>
    public async Task RemoveAsync(string repositoryPath, string branch, CancellationToken cancellationToken = default)
    {
        var parents = await ReadConfigAsync(repositoryPath, ParentKey, cancellationToken);
        var bases = await ReadConfigAsync(repositoryPath, BaseKey, cancellationToken);
        if (parents.GetValueOrDefault(branch) is { } parent && parents.FirstOrDefault(pair => pair.Value == branch).Key is { } child)
        {
            await SetAsync(repositoryPath, child, parent, bases.GetValueOrDefault(child), cancellationToken);
        }

        await _git.RunAsync(repositoryPath, ["config", "--unset", $"branch.{branch}.{ParentKey}"], cancellationToken: cancellationToken);
        await _git.RunAsync(repositoryPath, ["config", "--unset", $"branch.{branch}.{BaseKey}"], cancellationToken: cancellationToken);
    }

    /// <summary>
    /// After the bottom <paramref name="mergedCount"/> branches were (squash) merged into the trunk on GitHub:
    /// fetches, fast-forwards the local trunk, removes the merged branches (here and on GitHub, if still there),
    /// and rebases the rest onto the trunk without the merged commits. A conflict stops it like any rebase;
    /// after Continue, <see cref="RestackAsync"/> finishes the rest. Returns <see cref="RebaseOutcome.UpToDate"/>
    /// when the whole stack was merged (nothing left to rebase).
    /// </summary>
    /// <param name="returnTo">The branch to end on; when it was merged, the new bottom (or the trunk) instead.</param>
    public async Task<RebaseOutcome> SyncMergedAsync(
        string repositoryPath,
        Stack stack,
        int mergedCount,
        string? returnTo,
        bool autoStash,
        string remote = "origin",
        CancellationToken cancellationToken = default)
    {
        if (mergedCount <= 0 || mergedCount > stack.Branches.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(mergedCount));
        }

        var trunk = stack.Trunk;
        await _git.RunCheckedAsync(repositoryPath, ["fetch", "--quiet", "--prune", remote], cancellationToken: cancellationToken);
        await FastForwardAsync(repositoryPath, trunk, $"{remote}/{trunk}", cancellationToken);

        var merged = stack.Branches.Take(mergedCount).ToList();
        var rest = stack.Branches.Skip(mergedCount).ToList();

        // Don't stand on a branch that's about to be deleted.
        var current = (await _git.RunCheckedAsync(repositoryPath, ["branch", "--show-current"], cancellationToken: cancellationToken)).StandardOutput.Trim();
        var mergedNames = merged.Select(branch => branch.Name).ToHashSet(StringComparer.Ordinal);
        var endOn = returnTo is not null && mergedNames.Contains(returnTo) ? rest.FirstOrDefault()?.Name ?? trunk : returnTo;
        if (mergedNames.Contains(current))
        {
            await _git.RunCheckedAsync(repositoryPath, ["switch", "--quiet", endOn ?? trunk], cancellationToken: cancellationToken);
        }

        // The new bottom's own commits start where the last merged branch ended (or at its recorded base,
        // if that branch changed after the new bottom was built on it).
        if (rest.FirstOrDefault() is { } bottom)
        {
            var lastMerged = merged[^1];
            var upstream = bottom.NeedsRestack
                ? bottom.Base ?? await ForkPointAsync(repositoryPath, lastMerged.Name, bottom.Name, cancellationToken)
                : await TipAsync(repositoryPath, lastMerged.Name, cancellationToken);
            await SetAsync(repositoryPath, bottom.Name, trunk, upstream, cancellationToken);
        }

        foreach (var branch in merged)
        {
            await _git.RunCheckedAsync(repositoryPath, ["branch", "--quiet", "--delete", "--force", branch.Name], cancellationToken: cancellationToken);
            var onRemote = await _git.RunAsync(repositoryPath, ["rev-parse", "--verify", "--quiet", $"refs/remotes/{remote}/{branch.Name}"], cancellationToken: cancellationToken);
            if (onRemote.Succeeded)
            {
                await _git.RunAsync(repositoryPath, ["push", "--quiet", remote, "--delete", branch.Name], cancellationToken: cancellationToken);
            }
        }

        if (rest.Count == 0)
        {
            return RebaseOutcome.UpToDate;
        }

        var remaining = (await GetStacksAsync(repositoryPath, cancellationToken)).First(other => other.Contains(rest[0].Name));
        return await RestackAsync(repositoryPath, remaining, endOn, autoStash, cancellationToken);
    }

    // Moves the local trunk up to what was fetched; refuses when it has commits GitHub doesn't.
    private async Task FastForwardAsync(string repositoryPath, string branch, string fetched, CancellationToken cancellationToken)
    {
        var target = await _git.RunAsync(repositoryPath, ["rev-parse", "--verify", "--quiet", fetched], cancellationToken: cancellationToken);
        if (!target.Succeeded)
        {
            throw new InvalidOperationException($"{fetched} isn't on GitHub, so there's nothing to sync with.");
        }

        var local = await TipAsync(repositoryPath, branch, cancellationToken);
        var to = target.StandardOutput.Trim();
        if (local == to)
        {
            return;
        }

        if (!await IsAncestorAsync(repositoryPath, local, to, cancellationToken))
        {
            throw new InvalidOperationException($"Your {branch} has commits that aren't on GitHub. Push or move them first, then sync.");
        }

        var current = (await _git.RunCheckedAsync(repositoryPath, ["branch", "--show-current"], cancellationToken: cancellationToken)).StandardOutput.Trim();
        string[] arguments = current == branch ? ["merge", "--quiet", "--ff-only", fetched] : ["update-ref", $"refs/heads/{branch}", to, local];
        await _git.RunCheckedAsync(repositoryPath, arguments, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Replays every branch whose parent changed onto the parent's new version, bottom up. Each step is one
    /// rebase (--update-refs) that also carries along the branches above it that still sit on each other,
    /// so the usual case (a lower branch amended) is a single rebase. A conflict stops it like any rebase:
    /// resolve, Continue, then call this again for whatever is left. Ends on <paramref name="returnTo"/>.
    /// </summary>
    public async Task<RebaseOutcome> RestackAsync(string repositoryPath, Stack stack, string? returnTo, bool autoStash, CancellationToken cancellationToken = default)
    {
        var outcome = RebaseOutcome.UpToDate;

        // At most one step per branch; recomputed after each, since every step moves branches.
        for (var step = 0; step < stack.Branches.Count; step++)
        {
            var lowestIndex = stack.Branches.ToList().FindIndex(branch => branch.NeedsRestack);
            if (lowestIndex < 0)
            {
                break;
            }

            var lowest = stack.Branches[lowestIndex];

            // The branches above that still sit on each other move along in the same rebase.
            var topIndex = lowestIndex;
            while (topIndex + 1 < stack.Branches.Count && !stack.Branches[topIndex + 1].NeedsRestack)
            {
                topIndex++;
            }

            // Where the lowest branch's own commits start: its recorded base, else git's guess from the reflog.
            var upstream = lowest.Base ?? await ForkPointAsync(repositoryPath, lowest.Parent, lowest.Name, cancellationToken);

            // The branches below (and the parent) must not move; remember them to make sure.
            var below = new Dictionary<string, string>();
            foreach (var name in stack.Branches.Take(lowestIndex).Select(branch => branch.Name).Append(lowest.Parent).Distinct())
            {
                below[name] = await TipAsync(repositoryPath, name, cancellationToken);
            }

            outcome = await _rebase.RebaseStackAsync(
                repositoryPath, lowest.Parent, upstream, stack.Branches[topIndex].Name, returnTo, autoStash && step == 0, cancellationToken);

            await RestoreAsync(repositoryPath, below, cancellationToken);
            if (outcome != RebaseOutcome.Done)
            {
                return outcome;
            }

            await RecordBasesAsync(repositoryPath, cancellationToken);
            stack = (await GetStacksAsync(repositoryPath, cancellationToken)).FirstOrDefault(other => other.Contains(lowest.Name)) ?? stack;
        }

        return outcome;
    }

    /// <summary>
    /// Pushes every branch of the stack in one go: new ones are published, the rest force-pushed with lease
    /// (restacking changes commit ids, and the lease refuses to overwrite commits you haven't fetched).
    /// All branches go or none does.
    /// </summary>
    public async Task<GitResult> PushAsync(string repositoryPath, Stack stack, string remote = "origin", CancellationToken cancellationToken = default)
    {
        string[] arguments = ["push", "--atomic", "--force-with-lease", "--set-upstream", remote, .. stack.Branches.Select(branch => branch.Name)];
        return await _git.RunAsync(repositoryPath, arguments, cancellationToken: cancellationToken);
    }

    // Puts back any of these branches that moved (they're below what was restacked, so they never should).
    private async Task RestoreAsync(string repositoryPath, Dictionary<string, string> tips, CancellationToken cancellationToken)
    {
        foreach (var (branch, tip) in tips)
        {
            if (await TryTipAsync(repositoryPath, branch, cancellationToken) is { } now && now != tip)
            {
                await _git.RunCheckedAsync(repositoryPath, ["update-ref", $"refs/heads/{branch}", tip, now], cancellationToken: cancellationToken);
            }
        }
    }

    /// <summary>For every stacked branch that sits on its parent's tip, records that tip as its base.</summary>
    public async Task RecordBasesAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var parents = await ReadConfigAsync(repositoryPath, ParentKey, cancellationToken);
        var bases = await ReadConfigAsync(repositoryPath, BaseKey, cancellationToken);

        foreach (var (branch, parent) in parents)
        {
            var parentTip = await TryTipAsync(repositoryPath, parent, cancellationToken);
            if (parentTip is null || bases.GetValueOrDefault(branch) == parentTip)
            {
                continue;
            }

            if (await IsAncestorAsync(repositoryPath, parentTip, branch, cancellationToken))
            {
                await _git.RunCheckedAsync(repositoryPath, ["config", $"branch.{branch}.{BaseKey}", parentTip], cancellationToken: cancellationToken);
            }
        }
    }

    private async Task<StackEntry> DescribeAsync(string repositoryPath, string branch, string parent, string? @base, CancellationToken cancellationToken)
    {
        var parentTip = await TipAsync(repositoryPath, parent, cancellationToken);
        var onParent = await IsAncestorAsync(repositoryPath, parentTip, branch, cancellationToken);
        if (onParent && @base != parentTip)
        {
            await _git.RunCheckedAsync(repositoryPath, ["config", $"branch.{branch}.{BaseKey}", parentTip], cancellationToken: cancellationToken);
            @base = parentTip;
        }

        var from = onParent ? parentTip : @base ?? parentTip;
        var count = await _git.RunAsync(repositoryPath, ["rev-list", "--count", $"{from}..{branch}"], cancellationToken: cancellationToken);
        var commits = count.Succeeded && int.TryParse(count.StandardOutput.Trim(), out var number) ? number : 0;

        return new StackEntry(branch, parent, @base, commits, NeedsRestack: !onParent);
    }

    private async Task<Dictionary<string, string>> ReadConfigAsync(string repositoryPath, string key, CancellationToken cancellationToken)
    {
        // "branch.feature/x.mygit-parent main"
        var result = await _git.RunAsync(repositoryPath, ["config", "--local", "--get-regexp", $@"^branch\..*\.{key}$"], cancellationToken: cancellationToken);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!result.Succeeded)
        {
            return values; // none set
        }

        foreach (var line in result.OutputLines)
        {
            var space = line.IndexOf(' ');
            if (space < 0)
            {
                continue;
            }

            var name = line[..space];
            var branch = name["branch.".Length..^(key.Length + 1)];
            values[branch] = line[(space + 1)..].Trim();
        }

        return values;
    }

    private async Task SetAsync(string repositoryPath, string branch, string parent, string? @base, CancellationToken cancellationToken)
    {
        await _git.RunCheckedAsync(repositoryPath, ["config", $"branch.{branch}.{ParentKey}", parent], cancellationToken: cancellationToken);
        if (@base is not null)
        {
            await _git.RunCheckedAsync(repositoryPath, ["config", $"branch.{branch}.{BaseKey}", @base], cancellationToken: cancellationToken);
        }
    }

    private async Task<string> ForkPointAsync(string repositoryPath, string parent, string branch, CancellationToken cancellationToken)
    {
        var forkPoint = await _git.RunAsync(repositoryPath, ["merge-base", "--fork-point", parent, branch], cancellationToken: cancellationToken);
        if (forkPoint.Succeeded && forkPoint.StandardOutput.Trim().Length > 0)
        {
            return forkPoint.StandardOutput.Trim();
        }

        return (await _git.RunCheckedAsync(repositoryPath, ["merge-base", parent, branch], cancellationToken: cancellationToken)).StandardOutput.Trim();
    }

    private async Task<bool> IsAncestorAsync(string repositoryPath, string ancestor, string branch, CancellationToken cancellationToken) =>
        (await _git.RunAsync(repositoryPath, ["merge-base", "--is-ancestor", ancestor, branch], cancellationToken: cancellationToken)).Succeeded;

    private async Task<string> TipAsync(string repositoryPath, string branch, CancellationToken cancellationToken) =>
        (await _git.RunCheckedAsync(repositoryPath, ["rev-parse", $"refs/heads/{branch}"], cancellationToken: cancellationToken)).StandardOutput.Trim();

    private async Task<string?> TryTipAsync(string repositoryPath, string branch, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"], cancellationToken: cancellationToken);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }
}

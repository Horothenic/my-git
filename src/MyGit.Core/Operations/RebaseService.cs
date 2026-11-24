using System.Text;
using MyGit.Core.Git;

namespace MyGit.Core.Operations;

public enum RebaseOutcome
{
    Done,

    /// <summary>Stopped on a conflict: resolve, then continue (or skip / abort).</summary>
    Conflict,

    /// <summary>Nothing to replay: the branch already contains everything.</summary>
    UpToDate,

    /// <summary>Stopped at an "edit" step: change the commit if you like, then continue.</summary>
    Stopped,
}

/// <summary>
/// Rebases the current branch onto another branch or commit, plainly or with a plan
/// (reorder, reword, squash, fixup, drop, edit).
/// </summary>
public sealed class RebaseService
{
    private static readonly IReadOnlyDictionary<string, string> NoEditor = new Dictionary<string, string> { ["GIT_EDITOR"] = "true" };
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly IGitRunner _git;

    public RebaseService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>How many of your commits would be replayed onto <paramref name="onto"/>.</summary>
    public async Task<int> CountCommitsToReplayAsync(string repositoryPath, string onto, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-list", "--count", $"{onto}..HEAD"], cancellationToken: cancellationToken);
        return result.Succeeded && int.TryParse(result.StandardOutput.Trim(), out var count) ? count : 0;
    }

    /// <summary>
    /// Replays the current branch's commits on top of <paramref name="onto"/>.
    /// With <paramref name="autoStash"/>, uncommitted changes (new files too) are stashed first and
    /// brought back automatically when the rebase finishes or is aborted.
    /// </summary>
    public Task<RebaseOutcome> RebaseAsync(string repositoryPath, string onto, bool autoStash, CancellationToken cancellationToken = default) =>
        RunRebaseAsync(repositoryPath, ["rebase", onto], NoEditor, autoStash, $"rebasing onto {onto}", cancellationToken);

    /// <summary>
    /// Restacks: replays the commits after <paramref name="upstream"/> up to <paramref name="top"/> onto
    /// <paramref name="onto"/>, moving every branch in between along (--update-refs). Afterwards it switches back
    /// to <paramref name="returnTo"/> (before auto-stashed changes come back, so they land where they were).
    /// </summary>
    public Task<RebaseOutcome> RebaseStackAsync(
        string repositoryPath,
        string onto,
        string upstream,
        string top,
        string? returnTo,
        bool autoStash,
        CancellationToken cancellationToken = default) =>
        RunRebaseAsync(repositoryPath, ["rebase", "--update-refs", "--onto", onto, upstream, top], NoEditor, autoStash, $"restacking {top}", cancellationToken, returnTo);

    /// <summary>
    /// The commits an interactive rebase onto <paramref name="upstream"/> replays, oldest first
    /// (the order git replays them). With a null upstream: every commit, from the first one.
    /// Merge commits are left out: the rebase flattens them.
    /// </summary>
    public async Task<IReadOnlyList<RebaseCommit>> GetCommitsToRebaseAsync(string repositoryPath, string? upstream, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunCheckedAsync(
            repositoryPath,
            ["log", "--reverse", "--no-merges", "--format=%H%x1f%h%x1f%s%x1f%B%x1e", RangeFor(upstream)],
            cancellationToken: cancellationToken);

        return result.StandardOutput
            .Split('\x1e', StringSplitOptions.RemoveEmptyEntries)
            .Select(record => record.TrimStart('\n', '\r').Split('\x1f'))
            .Where(fields => fields.Length == 4)
            .Select(fields => new RebaseCommit(fields[0], fields[1], fields[2], fields[3].Trim()))
            .ToList();
    }

    /// <summary>True when the range has merge commits (the rebase flattens them).</summary>
    public async Task<bool> HasMergesToFlattenAsync(string repositoryPath, string? upstream, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["rev-list", "--merges", "--max-count=1", RangeFor(upstream)], cancellationToken: cancellationToken);
        return result.Succeeded && result.StandardOutput.Trim().Length > 0;
    }

    /// <summary>
    /// The parent of <paramref name="commit"/>, for "interactive rebase from here" (null for the very first commit).
    /// Throws <see cref="InvalidOperationException"/> when the commit isn't part of the current branch.
    /// </summary>
    public async Task<string?> GetUpstreamForAsync(string repositoryPath, string commit, CancellationToken cancellationToken = default)
    {
        var onBranch = await _git.RunAsync(repositoryPath, ["merge-base", "--is-ancestor", commit, "HEAD"], cancellationToken: cancellationToken);
        if (!onBranch.Succeeded)
        {
            throw new InvalidOperationException("That commit isn't part of the current branch, so there's nothing to rebase from there.");
        }

        var parent = await _git.RunAsync(repositoryPath, ["rev-parse", "--verify", "--quiet", $"{commit}^"], cancellationToken: cancellationToken);
        return parent.Succeeded ? parent.StandardOutput.Trim() : null;
    }

    /// <summary>
    /// Replays the plan's commits, in the plan's order, onto <paramref name="upstream"/> (null: from the first commit).
    /// git never opens an editor: the app writes the whole plan, and new messages are applied right after their commit.
    /// Conflicts and edit stops are then continued like any rebase (<see cref="OperationService"/>).
    /// </summary>
    public async Task<RebaseOutcome> RebaseInteractiveAsync(
        string repositoryPath,
        string? upstream,
        IReadOnlyList<RebaseStep> plan,
        bool autoStash,
        CancellationToken cancellationToken = default)
    {
        var folder = Path.Combine(await GitDirAsync(repositoryPath, cancellationToken), "mygit-rebase");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        var todo = Path.Combine(folder, "todo.txt");
        await File.WriteAllTextAsync(todo, BuildTodo(plan, folder), Utf8, cancellationToken);

        var environment = new Dictionary<string, string>
        {
            ["GIT_EDITOR"] = "true",

            // git runs this with the path of its own todo list added at the end: our plan replaces it.
            ["GIT_SEQUENCE_EDITOR"] = $"cp {ShellPath(todo)}",
        };

        string[] arguments = upstream is null ? ["rebase", "-i", "--root"] : ["rebase", "-i", upstream];
        return await RunRebaseAsync(repositoryPath, arguments, environment, autoStash, "an interactive rebase", cancellationToken);
    }

    /// <summary>
    /// The todo list git follows. Rewords and squashes become "pick" / "fixup" followed by an amend that
    /// reads the new message from a file, so messages never go through an editor or the command line.
    /// </summary>
    /// <param name="messageFolder">Where the message files are written.</param>
    public static string BuildTodo(IReadOnlyList<RebaseStep> plan, string messageFolder)
    {
        var kept = plan.Where(step => step.Action != RebaseAction.Drop).ToList();
        if (kept.Count > 0 && kept[0].Action is RebaseAction.Squash or RebaseAction.Fixup)
        {
            throw new ArgumentException("The first commit can't be squashed or fixed up: there's no commit above it to join.", nameof(plan));
        }

        var lines = new List<string>();

        // The commit a squash/fixup group joins, the message it ends up with, and whether that differs from its own.
        var groupMessage = new List<string>();
        var needsNewMessage = false;
        var messageCount = 0;

        void FinishGroup()
        {
            if (needsNewMessage)
            {
                var file = Path.Combine(messageFolder, $"message-{++messageCount}.txt");
                File.WriteAllText(file, string.Join("\n\n", groupMessage) + "\n", Utf8);
                lines.Add($"exec git commit --amend --allow-empty --no-verify --quiet -F {ShellPath(file)}");
            }

            groupMessage.Clear();
            needsNewMessage = false;
        }

        foreach (var step in plan)
        {
            var sha = step.Commit.Sha;
            switch (step.Action)
            {
                case RebaseAction.Drop:
                    lines.Add($"drop {sha}");
                    break;

                case RebaseAction.Fixup:
                    lines.Add($"fixup {sha}");
                    break;

                case RebaseAction.Squash:
                    lines.Add($"fixup {sha}");
                    groupMessage.Add((step.NewMessage ?? step.Commit.Message).Trim());
                    needsNewMessage = true;
                    break;

                default:
                    FinishGroup();
                    lines.Add($"{(step.Action == RebaseAction.Edit ? "edit" : "pick")} {sha}");

                    var reworded = step.Action == RebaseAction.Reword &&
                                   step.NewMessage is { } message &&
                                   message.Trim().Length > 0 &&
                                   message.Trim() != step.Commit.Message.Trim();
                    groupMessage.Add((reworded ? step.NewMessage! : step.Commit.Message).Trim());
                    needsNewMessage = reworded;
                    break;
            }
        }

        FinishGroup();
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// After a rebase that stopped, the stash made before it should come back once the rebase ends
    /// (continued to the end, or aborted). This puts it back if it's still waiting; call it then.
    /// Returns false when the stash couldn't go back cleanly (it then stays in the stash list).
    /// </summary>
    public async Task<bool> RestorePendingStashAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var note = await PendingStashFileAsync(repositoryPath, cancellationToken);
        if (!File.Exists(note))
        {
            return true;
        }

        var sha = (await File.ReadAllTextAsync(note, cancellationToken)).Trim();
        File.Delete(note);

        // Find it by id (its position in the stash list may have changed) and pop it.
        var list = await _git.RunAsync(repositoryPath, ["stash", "list", "--format=%gd %H"], cancellationToken: cancellationToken);
        var entry = list.OutputLines.FirstOrDefault(line => line.EndsWith(sha, StringComparison.Ordinal))?.Split(' ')[0];
        if (entry is null)
        {
            return true; // already gone (applied by hand)
        }

        var pop = await _git.RunAsync(repositoryPath, ["stash", "pop", entry], cancellationToken: cancellationToken);
        return pop.Succeeded;
    }

    /// <summary>True when a rebase started with auto-stash is still holding the stash.</summary>
    public async Task<bool> HasPendingStashAsync(string repositoryPath, CancellationToken cancellationToken = default) =>
        File.Exists(await PendingStashFileAsync(repositoryPath, cancellationToken));

    // Stashes first (new files too) when asked, runs the rebase, and brings the stash back when it ends,
    // or remembers it for when a stopped rebase ends later.
    private async Task<RebaseOutcome> RunRebaseAsync(
        string repositoryPath,
        string[] arguments,
        IReadOnlyDictionary<string, string> environment,
        bool autoStash,
        string label,
        CancellationToken cancellationToken,
        string? returnTo = null)
    {
        // git's own --autostash leaves new (untracked) files behind; stash them too so they come along.
        var stashed = false;
        if (autoStash)
        {
            var push = await _git.RunCheckedAsync(
                repositoryPath,
                ["stash", "push", "--include-untracked", "--message", $"My Git: before {label}"],
                cancellationToken: cancellationToken);
            stashed = !push.StandardOutput.Contains("No local changes", StringComparison.OrdinalIgnoreCase);
        }

        // Restacking: --update-refs skips a branch that's checked out, so nothing may be while it runs.
        if (returnTo is not null)
        {
            await _git.RunCheckedAsync(repositoryPath, ["switch", "--quiet", "--detach"], cancellationToken: cancellationToken);
        }

        var result = await _git.RunAsync(repositoryPath, arguments, environment, cancellationToken);

        if (await IsRebaseInProgressAsync(repositoryPath, cancellationToken))
        {
            if (stashed)
            {
                await RememberStashAsync(repositoryPath, cancellationToken);
            }

            return result.Succeeded ? RebaseOutcome.Stopped : RebaseOutcome.Conflict;
        }

        if (returnTo is not null)
        {
            await _git.RunCheckedAsync(repositoryPath, ["switch", "--quiet", returnTo], cancellationToken: cancellationToken);
        }

        if (stashed)
        {
            await _git.RunAsync(repositoryPath, ["stash", "pop"], cancellationToken: cancellationToken);
        }

        if (!result.Succeeded)
        {
            throw new GitCommandException(arguments, result);
        }

        var output = result.StandardOutput + result.StandardError;
        return output.Contains("is up to date", StringComparison.OrdinalIgnoreCase) ? RebaseOutcome.UpToDate : RebaseOutcome.Done;
    }

    private async Task<bool> IsRebaseInProgressAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var gitDir = await GitDirAsync(repositoryPath, cancellationToken);
        return Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply"));
    }

    private async Task RememberStashAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var top = await _git.RunCheckedAsync(repositoryPath, ["rev-parse", "stash@{0}"], cancellationToken: cancellationToken);
        await File.WriteAllTextAsync(await PendingStashFileAsync(repositoryPath, cancellationToken), top.StandardOutput.Trim(), cancellationToken);
    }

    // A note inside .git (never committed or pushed) instead of the repository's config.
    private async Task<string> PendingStashFileAsync(string repositoryPath, CancellationToken cancellationToken) =>
        Path.Combine(await GitDirAsync(repositoryPath, cancellationToken), "MYGIT_PENDING_STASH");

    private async Task<string> GitDirAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        var gitDir = await _git.RunCheckedAsync(repositoryPath, ["rev-parse", "--absolute-git-dir"], cancellationToken: cancellationToken);
        return gitDir.StandardOutput.Trim();
    }

    private static string RangeFor(string? upstream) => upstream is null ? "HEAD" : $"{upstream}..HEAD";

    // A path for git's shell (sh): forward slashes, quoted.
    private static string ShellPath(string path) => $"\"{path.Replace('\\', '/')}\"";
}

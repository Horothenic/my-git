using System.Globalization;
using System.Text.RegularExpressions;
using MyGit.Core.Changes;
using MyGit.Core.Diffs;
using MyGit.Core.Git;
using MyGit.Core.History;

namespace MyGit.Core.Stashes;

/// <summary>A stash: changes put aside with "git stash".</summary>
/// <param name="Ref">e.g. "stash@{0}" (0 is the newest). Changes when stashes are added or removed.</param>
/// <param name="Branch">The branch it was made on.</param>
public sealed record StashEntry(string Ref, string Sha, string Message, string Branch, DateTimeOffset CreatedAt);

public enum StashApplyOutcome
{
    Done,

    /// <summary>Your current changes are in the way; nothing was changed and the stash is kept.</summary>
    Blocked,

    /// <summary>Applied, but some files have conflicts to resolve; the stash is kept.</summary>
    Conflict,
}

/// <summary>
/// Lists, creates, applies and drops stashes, and shows what's in them.
/// </summary>
public sealed partial class StashService
{
    private const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    private readonly IGitRunner _git;

    public StashService(IGitRunner git)
    {
        _git = git;
    }

    public async Task<IReadOnlyList<StashEntry>> ListAsync(string repositoryPath, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["stash", "list", "--format=%gd%x00%H%x00%gs%x00%ct"], cancellationToken: cancellationToken);
        if (!result.Succeeded)
        {
            return [];
        }

        var stashes = new List<StashEntry>();
        foreach (var line in result.OutputLines)
        {
            var fields = line.Split('\0');
            if (fields.Length < 4)
            {
                continue;
            }

            var (branch, message) = ParseSubject(fields[2]);
            stashes.Add(new StashEntry(
                fields[0],
                fields[1],
                message,
                branch,
                DateTimeOffset.FromUnixTimeSeconds(long.Parse(fields[3], CultureInfo.InvariantCulture)).ToLocalTime()));
        }

        return stashes;
    }

    /// <summary>
    /// Parses a stash subject: "On main: my message" (with -m) or "WIP on main: a1b2c3d last commit" (without).
    /// </summary>
    public static (string Branch, string Message) ParseSubject(string subject)
    {
        var match = Subject().Match(subject);
        if (!match.Success)
        {
            return (string.Empty, subject);
        }

        var branch = match.Groups["branch"].Value;
        var message = match.Groups["wip"].Success ? $"Work in progress on {branch}" : match.Groups["message"].Value;
        return (branch, message);
    }

    /// <summary>
    /// Puts uncommitted changes aside (including new files). With <paramref name="paths"/>, only those files.
    /// Returns false when there was nothing to stash.
    /// </summary>
    public async Task<bool> PushAsync(string repositoryPath, string? message, IReadOnlyCollection<string>? paths = null, CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["stash", "push", "--include-untracked"];
        if (!string.IsNullOrWhiteSpace(message))
        {
            arguments.AddRange(["--message", message.Trim()]);
        }

        if (paths is { Count: > 0 })
        {
            arguments.Add("--");
            arguments.AddRange(paths);
        }

        var result = await _git.RunCheckedAsync(repositoryPath, arguments, cancellationToken: cancellationToken);
        return !result.StandardOutput.Contains("No local changes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Applies a stash to the working tree. With <paramref name="remove"/> ("pop") it's deleted afterwards.</summary>
    public async Task<StashApplyOutcome> ApplyAsync(string repositoryPath, StashEntry stash, bool remove, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["stash", remove ? "pop" : "apply", stash.Ref], cancellationToken: cancellationToken);

        if (result.Succeeded)
        {
            return StashApplyOutcome.Done;
        }

        var output = result.StandardOutput + result.StandardError;
        if (output.Contains("CONFLICT", StringComparison.Ordinal))
        {
            return StashApplyOutcome.Conflict;
        }

        if (output.Contains("would be overwritten", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return StashApplyOutcome.Blocked;
        }

        throw new GitCommandException(["stash", remove ? "pop" : "apply", stash.Ref], result);
    }

    public Task DropAsync(string repositoryPath, StashEntry stash, CancellationToken cancellationToken = default) =>
        _git.RunCheckedAsync(repositoryPath, ["stash", "drop", stash.Ref], cancellationToken: cancellationToken);

    /// <summary>
    /// The files in a stash: changed tracked files, plus new files (git keeps those in a third parent).
    /// </summary>
    public async Task<IReadOnlyList<FileChange>> GetFilesAsync(string repositoryPath, StashEntry stash, CancellationToken cancellationToken = default)
    {
        var tracked = await _git.RunCheckedAsync(
            repositoryPath,
            ["diff", "--name-status", "-z", "-M", $"{stash.Sha}^1", stash.Sha],
            cancellationToken: cancellationToken);
        var files = HistoryService.ParseNameStatus(tracked.StandardOutput).ToList();

        if (await HasUntrackedPartAsync(repositoryPath, stash, cancellationToken))
        {
            var untracked = await _git.RunCheckedAsync(
                repositoryPath,
                ["diff", "--name-status", "-z", EmptyTree, $"{stash.Sha}^3"],
                cancellationToken: cancellationToken);
            files.AddRange(HistoryService.ParseNameStatus(untracked.StandardOutput));
        }

        return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The diff of one file in a stash.</summary>
    public async Task<FileDiff> GetFileDiffAsync(string repositoryPath, StashEntry stash, FileChange file, CancellationToken cancellationToken = default)
    {
        // A new file lives in the stash's untracked part (^3); a changed one compares with where it was made (^1).
        var inTracked = await _git.RunAsync(repositoryPath, ["cat-file", "-e", $"{stash.Sha}:{file.Path}"], cancellationToken: cancellationToken);
        var (from, to) = inTracked.Succeeded || !await HasUntrackedPartAsync(repositoryPath, stash, cancellationToken)
            ? ($"{stash.Sha}^1", stash.Sha)
            : (EmptyTree, $"{stash.Sha}^3");

        var result = await _git.RunCheckedAsync(
            repositoryPath,
            ["diff", "--no-color", "--no-ext-diff", "-U3", "-M", from, to, "--", file.Path],
            cancellationToken: cancellationToken);
        return DiffParser.Parse(file.Path, result.StandardOutput);
    }

    private async Task<bool> HasUntrackedPartAsync(string repositoryPath, StashEntry stash, CancellationToken cancellationToken)
    {
        var third = await _git.RunAsync(repositoryPath, ["rev-parse", "--verify", "-q", $"{stash.Sha}^3"], cancellationToken: cancellationToken);
        return third.Succeeded;
    }

    [GeneratedRegex(@"^(?:(?<wip>WIP on)|On) (?<branch>[^:]+):\s?(?<message>.*)$")]
    private static partial Regex Subject();
}

using System.Globalization;
using MyGit.Core.Git;

namespace MyGit.Core.PullRequests;

/// <summary>A pull request template from the repository, e.g. ".github/pull_request_template.md".</summary>
/// <param name="Name">What the picker shows: "Default" for the single template, else the file name without ".md".</param>
public sealed record PullRequestTemplate(string Name, string Path, string Content);

/// <summary>
/// What a new pull request starts with: a title from the commits or the branch name, the commit list,
/// and the repository's PR templates. GitHub only fills in templates on its own web page, so we do it here.
/// </summary>
public sealed class PullRequestDraftService
{
    private const string TemplateFile = "pull_request_template.md";
    private const string TemplateFolder = "pull_request_template";

    private readonly IGitRunner _git;

    public PullRequestDraftService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// The title: the commit's subject when there's one commit, otherwise the branch name made readable
    /// ("feature/inventory-slots" becomes "Inventory slots").
    /// </summary>
    public static string TitleFor(string branch, IReadOnlyList<string> subjects) =>
        subjects.Count == 1 ? subjects[0] : TitleFromBranch(branch);

    public static string TitleFromBranch(string branch)
    {
        var name = branch[(branch.LastIndexOf('/') + 1)..].Replace('-', ' ').Replace('_', ' ').Trim();
        return name.Length == 0 ? branch : char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..];
    }

    /// <summary>"- subject" per commit, oldest first.</summary>
    public static string CommitList(IReadOnlyList<string> subjects) => string.Join("\n", subjects.Select(subject => $"- {subject}"));

    /// <summary>The subjects of the commits on HEAD that <paramref name="baseRef"/> doesn't have, oldest first.</summary>
    public async Task<IReadOnlyList<string>> GetCommitSubjectsAsync(string repositoryPath, string baseRef, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(repositoryPath, ["log", "--reverse", "--no-merges", "--format=%s", $"{baseRef}..HEAD"], cancellationToken: cancellationToken);
        return result.Succeeded ? result.OutputLines : [];
    }

    /// <summary>
    /// The PR templates on <paramref name="baseRef"/> (falling back to HEAD when that branch isn't known locally),
    /// in the places GitHub looks, ignoring case: pull_request_template.md in the root, .github/ or docs/,
    /// and every .md in .github/PULL_REQUEST_TEMPLATE/. The single template comes first.
    /// </summary>
    public async Task<IReadOnlyList<PullRequestTemplate>> FindTemplatesAsync(string repositoryPath, string baseRef, CancellationToken cancellationToken = default)
    {
        var verify = await _git.RunAsync(repositoryPath, ["rev-parse", "--verify", "--quiet", $"{baseRef}^{{commit}}"], cancellationToken: cancellationToken);
        var revision = verify.Succeeded ? baseRef : "HEAD";

        var singles = new List<string>();
        var several = new List<string>();

        var root = await ListAsync(repositoryPath, revision, string.Empty, cancellationToken);
        singles.AddRange(Matching(root, TemplateFile, isFolder: false).Select(entry => entry.Name));

        foreach (var folder in Matching(root, ".github", isFolder: true).Concat(Matching(root, "docs", isFolder: true)))
        {
            var entries = await ListAsync(repositoryPath, revision, folder.Name, cancellationToken);
            singles.AddRange(Matching(entries, TemplateFile, isFolder: false).Select(entry => $"{folder.Name}/{entry.Name}"));

            if (folder.Name.Equals(".github", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var templates in Matching(entries, TemplateFolder, isFolder: true))
                {
                    var path = $"{folder.Name}/{templates.Name}";
                    several.AddRange((await ListAsync(repositoryPath, revision, path, cancellationToken))
                        .Where(entry => !entry.IsFolder && entry.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        .Select(entry => $"{path}/{entry.Name}")
                        .Order(StringComparer.OrdinalIgnoreCase));
                }
            }
        }

        var found = new List<PullRequestTemplate>();
        foreach (var path in singles.Take(1).Concat(several))
        {
            var content = await _git.RunAsync(repositoryPath, ["show", $"{revision}:{path}"], cancellationToken: cancellationToken);
            if (content.Succeeded)
            {
                var name = several.Contains(path) ? Path.GetFileNameWithoutExtension(path) : "Default";
                found.Add(new PullRequestTemplate(name, path, content.StandardOutput.ReplaceLineEndings("\n").TrimEnd() + "\n"));
            }
        }

        return found;
    }

    private static IEnumerable<(string Name, bool IsFolder)> Matching(IEnumerable<(string Name, bool IsFolder)> entries, string name, bool isFolder) =>
        entries.Where(entry => entry.IsFolder == isFolder && entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    // The entries of one folder in a commit (no recursion: repositories can be big).
    private async Task<List<(string Name, bool IsFolder)>> ListAsync(string repositoryPath, string revision, string folder, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(repositoryPath, ["ls-tree", $"{revision}:{folder}"], cancellationToken: cancellationToken);
        if (!result.Succeeded)
        {
            return [];
        }

        // "<mode> <type> <sha>\t<name>"
        return result.OutputLines
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => (parts[1], parts[0].Contains(" tree ", StringComparison.Ordinal)))
            .ToList();
    }
}

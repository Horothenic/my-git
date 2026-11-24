using MyGit.Core.Git;
using MyGit.Core.PullRequests;

namespace MyGit.Core.Tests.PullRequests;

public sealed class PullRequestDraftTests
{
    private readonly PullRequestDraftService _drafts = new(new GitRunner());

    private static async Task CommitAsync(TempRepository repository, string path, string content, string message)
    {
        var full = Path.Combine(repository.Path, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
        await repository.RunAsync("add", path);
        await repository.RunAsync("commit", "-m", message);
    }

    [Theory]
    [InlineData("feature/inventory-slots", "Inventory slots")]
    [InlineData("fix_save_crash", "Fix save crash")]
    [InlineData("users/ivan/ABC-123-bag-ui", "ABC 123 bag ui")]
    public void TitleFromBranch_MakesTheNameReadable(string branch, string title)
    {
        Assert.Equal(title, PullRequestDraftService.TitleFromBranch(branch));
    }

    [Fact]
    public void Title_IsTheCommitSubject_WhenThereIsOneCommit()
    {
        Assert.Equal("Add slot limit", PullRequestDraftService.TitleFor("feature/slots", ["Add slot limit"]));
        Assert.Equal("Slots", PullRequestDraftService.TitleFor("feature/slots", ["one", "two"]));
        Assert.Equal("- one\n- two", PullRequestDraftService.CommitList(["one", "two"]));
    }

    [Fact]
    public async Task CommitSubjects_AreTheBranchsOwnCommits_OldestFirst()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("a.txt", "a", "base");
        await repository.RunAsync("checkout", "-q", "-b", "feature");
        await repository.CommitFileAsync("b.txt", "b", "first");
        await repository.CommitFileAsync("c.txt", "c", "second");

        Assert.Equal(["first", "second"], await _drafts.GetCommitSubjectsAsync(repository.Path, "main"));
    }

    [Fact]
    public async Task NoTemplate_FindsNothing()
    {
        using var repository = await TempRepository.CreateAsync();
        await repository.CommitFileAsync("README.md", "hi", "base");

        Assert.Empty(await _drafts.FindTemplatesAsync(repository.Path, "main"));
    }

    [Theory]
    [InlineData("pull_request_template.md")]
    [InlineData(".github/pull_request_template.md")]
    [InlineData(".github/PULL_REQUEST_TEMPLATE.md")]
    [InlineData("docs/Pull_Request_Template.md")]
    public async Task FindsTheSingleTemplate_InEachPlaceGitHubLooks(string path)
    {
        using var repository = await TempRepository.CreateAsync();
        await CommitAsync(repository, path, "## What\r\n\r\n## Why\r\n", "template");

        var template = Assert.Single(await _drafts.FindTemplatesAsync(repository.Path, "main"));

        Assert.Equal(("Default", path), (template.Name, template.Path));
        Assert.Equal("## What\n\n## Why\n", template.Content);
    }

    [Fact]
    public async Task FindsEveryTemplateInTheFolder_AfterTheSingleOne()
    {
        using var repository = await TempRepository.CreateAsync();
        await CommitAsync(repository, ".github/pull_request_template.md", "default", "single");
        await CommitAsync(repository, ".github/PULL_REQUEST_TEMPLATE/feature.md", "feature", "feature");
        await CommitAsync(repository, ".github/PULL_REQUEST_TEMPLATE/bugfix.md", "bugfix", "bugfix");
        await CommitAsync(repository, ".github/PULL_REQUEST_TEMPLATE/notes.txt", "not a template", "notes");

        var templates = await _drafts.FindTemplatesAsync(repository.Path, "main");

        Assert.Equal(["Default", "bugfix", "feature"], templates.Select(template => template.Name));
    }

    [Fact]
    public async Task UsesTheBaseBranchsTemplate_NotTheWorkingCopy()
    {
        using var repository = await TempRepository.CreateAsync();
        await CommitAsync(repository, ".github/pull_request_template.md", "from main", "template");
        await repository.RunAsync("checkout", "-q", "-b", "feature");
        await CommitAsync(repository, ".github/pull_request_template.md", "changed on feature", "edit template");

        Assert.Equal("from main\n", Assert.Single(await _drafts.FindTemplatesAsync(repository.Path, "main")).Content);

        // An unknown base (e.g. not fetched yet) falls back to what's checked out.
        Assert.Equal("changed on feature\n", Assert.Single(await _drafts.FindTemplatesAsync(repository.Path, "origin/nope")).Content);
    }
}

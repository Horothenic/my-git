using MyGit.Core.Clone;
using MyGit.Core.Git;

namespace MyGit.Core.Tests.Clone;

public sealed class CloneServiceTests : IDisposable
{
    private readonly string _folder = TempRepository.CreateEmptyFolder();
    private readonly CloneService _service = new(new GitRunner());

    [Theory]
    [InlineData("https://github.com/Horothenic/my-git.git", "my-git")]
    [InlineData("https://github.com/Horothenic/my-git", "my-git")]
    [InlineData("https://github.com/Horothenic/my-git/", "my-git")]
    [InlineData("git@github.com:Horothenic/my-git.git", "my-git")]
    [InlineData(@"C:\code\source-repo", "source-repo")]
    public void FolderNameFromUrl_MatchesGitDefault(string url, string expected)
    {
        Assert.Equal(expected, CloneService.FolderNameFromUrl(url));
    }

    [Fact]
    public void ValidateTarget_AllowsMissingOrEmptyFolder()
    {
        Assert.Null(CloneService.ValidateTarget(Path.Combine(_folder, "new")));
        Assert.Null(CloneService.ValidateTarget(Directory.CreateDirectory(Path.Combine(_folder, "empty")).FullName));
    }

    [Fact]
    public void ValidateTarget_RejectsNonEmptyFolder()
    {
        var target = Directory.CreateDirectory(Path.Combine(_folder, "used")).FullName;
        File.WriteAllText(Path.Combine(target, "file.txt"), "x");

        Assert.NotNull(CloneService.ValidateTarget(target));
    }

    [Fact]
    public async Task CloneAsync_ClonesAndReportsProgress()
    {
        using var source = await TempRepository.CreateAsync();
        await source.CommitFileAsync("readme.md", "hello", "first");
        var target = Path.Combine(_folder, "clone");
        var updates = new List<CloneProgress>();

        // A file:// URL makes git use the network transport, which prints progress like a real remote.
        await _service.CloneAsync(new Uri(source.Path).AbsoluteUri, target, new SynchronousProgress(updates.Add));

        Assert.True(File.Exists(Path.Combine(target, "readme.md")));
        Assert.Contains(updates, update => update.Percent == 100);
    }

    [Fact]
    public async Task CloneAsync_RemovesFolder_WhenCloneFails()
    {
        var target = Path.Combine(_folder, "clone");
        var missingSource = Path.Combine(_folder, "does-not-exist");

        await Assert.ThrowsAsync<GitCommandException>(() => _service.CloneAsync(missingSource, target));

        Assert.False(Directory.Exists(target));
    }

    public void Dispose() => TempRepository.DeleteFolder(_folder);

    /// <summary>Progress&lt;T&gt; posts to the thread pool; tests need the updates right away.</summary>
    private sealed class SynchronousProgress(Action<CloneProgress> report) : IProgress<CloneProgress>
    {
        public void Report(CloneProgress value) => report(value);
    }
}

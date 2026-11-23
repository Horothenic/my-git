using MyGit.Core.Git;

namespace MyGit.Core.Tests.Git;

public sealed class GitRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsOutput_WhenCommandSucceeds()
    {
        var runner = new GitRunner();

        var result = await runner.RunAsync(Path.GetTempPath(), ["--version"]);

        Assert.True(result.Succeeded);
        Assert.StartsWith("git version", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_ReturnsFailure_WithoutThrowing()
    {
        var folder = TempRepository.CreateEmptyFolder();
        try
        {
            var result = await new GitRunner().RunAsync(folder, ["rev-parse", "--show-toplevel"]);

            Assert.False(result.Succeeded);
            Assert.Contains("not a git repository", result.StandardError);
        }
        finally
        {
            TempRepository.DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task RunCheckedAsync_Throws_WhenCommandFails()
    {
        var folder = TempRepository.CreateEmptyFolder();
        try
        {
            var exception = await Assert.ThrowsAsync<GitCommandException>(
                () => new GitRunner().RunCheckedAsync(folder, ["rev-parse", "--show-toplevel"]));

            Assert.NotEqual(0, exception.Result.ExitCode);
            Assert.Equal(["rev-parse", "--show-toplevel"], exception.Arguments);
        }
        finally
        {
            TempRepository.DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task RunAsync_KeepsNonAsciiFileNamesReadable()
    {
        using var repository = await TempRepository.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "añadir.txt"), "hola");

        var result = await repository.RunAsync("status", "--porcelain");

        Assert.Contains("añadir.txt", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_PassesEnvironmentVariables()
    {
        using var repository = await TempRepository.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(repository.Path, "a.txt"), "a");
        await repository.RunAsync("add", "a.txt");

        var environment = new Dictionary<string, string> { ["GIT_AUTHOR_NAME"] = "Env Author" };
        await repository.Git.RunCheckedAsync(repository.Path, ["commit", "-m", "first"], environment);

        var log = await repository.RunAsync("log", "-1", "--format=%an");
        Assert.Equal("Env Author", log.StandardOutput.Trim());
    }
}

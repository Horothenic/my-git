using MyGit.Core.Clone;

namespace MyGit.Core.Tests.Clone;

public sealed class CloneProgressTests
{
    [Theory]
    [InlineData("Receiving objects:  64% (1290/2015), 41.20 MiB | 10.30 MiB/s", "Receiving objects", 64, "41.20 MiB | 10.30 MiB/s")]
    [InlineData("remote: Counting objects: 100% (512/512), done.", "Counting objects", 100, null)]
    [InlineData("Resolving deltas:  12% (40/330)", "Resolving deltas", 12, null)]
    [InlineData("Updating files: 100% (87/87), done.", "Updating files", 100, null)]
    public void Parse_ReadsProgressLines(string line, string phase, int percent, string? detail)
    {
        var progress = CloneProgress.Parse(line);

        Assert.NotNull(progress);
        Assert.Equal(phase, progress.Phase);
        Assert.Equal(percent, progress.Percent);
        Assert.Equal(detail, progress.Detail);
    }

    [Theory]
    [InlineData("Cloning into 'my-git'...")]
    [InlineData("remote: Enumerating objects: 512, done.")]
    [InlineData("")]
    public void Parse_ReturnsNull_ForOtherLines(string line)
    {
        Assert.Null(CloneProgress.Parse(line));
    }
}

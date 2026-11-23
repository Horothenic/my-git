using MyGit.Core.Changes;

namespace MyGit.Core.Tests.Changes;

public sealed class RepositoryWatcherTests
{
    [Theory]
    [InlineData("Assets/Inventory.cs", true)]
    [InlineData(".git/index", true)]
    [InlineData(".git/HEAD", true)]
    [InlineData(@".git\refs\heads\main", true)]
    [InlineData(".git/objects/ab/cdef", false)]
    [InlineData(".git/index.lock", false)]
    [InlineData(".git/logs/HEAD", false)]
    [InlineData(".gitignore", true)]
    public void Matters_FiltersGitInternals(string path, bool expected)
    {
        Assert.Equal(expected, RepositoryWatcher.Matters(path));
    }

    [Fact]
    public async Task Changed_FiresOnce_ForABurstOfChanges()
    {
        using var repository = await TempRepository.CreateAsync();
        using var watcher = new RepositoryWatcher(repository.Path);
        var count = 0;
        var fired = new TaskCompletionSource();
        watcher.Changed += () =>
        {
            Interlocked.Increment(ref count);
            fired.TrySetResult();
        };

        for (var i = 0; i < 20; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(repository.Path, $"file{i}.txt"), "x");
        }

        await fired.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(RepositoryWatcher.QuietPeriod * 2);

        Assert.Equal(1, count);
    }
}

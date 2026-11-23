using MyGit.Core.Git;

namespace MyGit.Core.Tests;

/// <summary>
/// A throwaway git repository in the temp folder, deleted when disposed.
/// </summary>
public sealed class TempRepository : IDisposable
{
    private TempRepository(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public GitRunner Git { get; } = new();

    public static async Task<TempRepository> CreateAsync(string initialBranch = "main")
    {
        var path = CreateEmptyFolder();
        var repository = new TempRepository(path);

        await repository.RunAsync("init", "-b", initialBranch);
        await repository.RunAsync("config", "user.name", "Test User");
        await repository.RunAsync("config", "user.email", "test@example.com");
        await repository.RunAsync("config", "commit.gpgsign", "false");

        return repository;
    }

    /// <summary>Creates an empty folder that is not a git repository.</summary>
    public static string CreateEmptyFolder()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mygit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public Task<GitResult> RunAsync(params string[] arguments) => Git.RunCheckedAsync(Path, arguments);

    public async Task CommitFileAsync(string fileName, string content, string message)
    {
        await File.WriteAllTextAsync(System.IO.Path.Combine(Path, fileName), content);
        await RunAsync("add", fileName);
        await RunAsync("commit", "-m", message);
    }

    public void Dispose() => DeleteFolder(Path);

    public static void DeleteFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        // git marks object files read-only, which blocks Directory.Delete on Windows.
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}

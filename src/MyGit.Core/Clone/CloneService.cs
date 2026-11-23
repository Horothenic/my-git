using MyGit.Core.Git;

namespace MyGit.Core.Clone;

/// <summary>
/// Clones repositories with "git clone", reporting progress and cleaning up on cancel or failure.
/// </summary>
public sealed class CloneService
{
    private readonly IGitRunner _git;

    public CloneService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>
    /// Folder name git would use for <paramref name="url"/>, e.g. "my-git" for
    /// "https://github.com/Horothenic/my-git.git".
    /// </summary>
    public static string FolderNameFromUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/', '\\');

        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^4];
        }

        var lastSeparator = trimmed.LastIndexOfAny(['/', '\\', ':']);
        return lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;
    }

    /// <summary>
    /// Returns a reason the clone can't go into <paramref name="targetPath"/>, or null if it can.
    /// </summary>
    public static string? ValidateTarget(string targetPath)
    {
        if (File.Exists(targetPath))
        {
            return "A file with that name already exists.";
        }

        if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
        {
            return "That folder already exists and isn't empty.";
        }

        return null;
    }

    /// <summary>
    /// Clones <paramref name="url"/> into <paramref name="targetPath"/>.
    /// If the clone fails or is cancelled, the partly created folder is removed.
    /// </summary>
    public async Task CloneAsync(
        string url,
        string targetPath,
        IProgress<CloneProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var problem = ValidateTarget(targetPath);
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(targetPath))!;
        Directory.CreateDirectory(parent);
        var existedBefore = Directory.Exists(targetPath);

        void OnLine(string line)
        {
            var update = CloneProgress.Parse(line);
            if (update is not null)
            {
                progress?.Report(update);
            }
        }

        try
        {
            await _git.RunCheckedAsync(
                parent,
                ["clone", "--progress", "--", url, targetPath],
                cancellationToken: cancellationToken,
                onProgress: OnLine);
        }
        catch
        {
            CleanUp(targetPath, existedBefore);
            throw;
        }
    }

    private static void CleanUp(string targetPath, bool existedBefore)
    {
        if (!Directory.Exists(targetPath))
        {
            return;
        }

        // git may still be releasing file handles right after it was killed.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                if (existedBefore)
                {
                    // The user picked an existing empty folder: empty it again, keep the folder.
                    foreach (var entry in Directory.EnumerateFileSystemEntries(targetPath))
                    {
                        if (Directory.Exists(entry))
                        {
                            Directory.Delete(entry, recursive: true);
                        }
                        else
                        {
                            File.Delete(entry);
                        }
                    }
                }
                else
                {
                    Directory.Delete(targetPath, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(200);
            }
        }
    }
}

namespace MyGit.Core.Repositories;

/// <summary>
/// A repository the user added to My Git.
/// </summary>
/// <param name="Path">Absolute path to the working tree root.</param>
/// <param name="Name">Display name, by default the folder name.</param>
public sealed record RepositoryInfo(string Path, string Name)
{
    public static RepositoryInfo FromPath(string path) =>
        new(path, System.IO.Path.GetFileName(path.TrimEnd('\\', '/')));
}

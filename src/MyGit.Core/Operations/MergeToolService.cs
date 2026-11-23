using MyGit.Core.Git;

namespace MyGit.Core.Operations;

/// <summary>A program that can resolve a conflicted file.</summary>
/// <param name="Name">Shown in the menu, e.g. "VS Code".</param>
/// <param name="Command">
/// The command git runs, with git's placeholders $BASE, $LOCAL, $REMOTE and $MERGED.
/// Null for the merge tool already set up in the user's git config.
/// </param>
public sealed record MergeTool(string Name, string? Command)
{
    public bool IsGitConfigured => Command is null;
}

public enum MergeToolOutcome
{
    /// <summary>The tool saved a result without conflict markers; the file is marked resolved.</summary>
    Resolved,

    /// <summary>The tool closed without finishing (or left conflict markers); the file is still conflicted.</summary>
    NotResolved,
}

/// <summary>
/// Finds merge tools on this machine and runs one on a conflicted file (through "git mergetool",
/// which prepares the base / ours / theirs versions and cleans up afterwards).
///
/// Unity's UnityYAMLMerge is only offered for Unity asset files: most repositories aren't Unity projects.
/// </summary>
public sealed class MergeToolService
{
    /// <summary>File types UnityYAMLMerge understands (Unity's serialized YAML assets).</summary>
    public static readonly IReadOnlySet<string> UnityExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".unity", ".prefab", ".asset", ".mat", ".anim", ".controller", ".overrideController", ".meta",
        ".physicMaterial", ".physicsMaterial2D", ".mask", ".playable", ".signal", ".lighting", ".spriteatlas",
    };

    private readonly IGitRunner _git;
    private IReadOnlyList<(string Name, string Path, bool UnityOnly)>? _installed;

    public MergeToolService(IGitRunner git)
    {
        _git = git;
    }

    /// <summary>Tools that make sense for <paramref name="path"/>, best first.</summary>
    public async Task<IReadOnlyList<MergeTool>> GetToolsForAsync(string repositoryPath, string path, CancellationToken cancellationToken = default)
    {
        var tools = new List<MergeTool>();
        var isUnityAsset = UnityExtensions.Contains(Path.GetExtension(path));

        foreach (var (name, exe, unityOnly) in Installed())
        {
            if (unityOnly && !isUnityAsset)
            {
                continue;
            }

            tools.Add(new MergeTool(name, CommandFor(name, exe)));
        }

        // Whatever the user set up for git (merge.tool), if anything.
        var configured = await _git.RunAsync(repositoryPath, ["config", "merge.tool"], cancellationToken: cancellationToken);
        if (configured.Succeeded && configured.StandardOutput.Trim() is { Length: > 0 } toolName)
        {
            tools.Add(new MergeTool($"{toolName} (from your git config)", null));
        }

        // Unity assets: UnityYAMLMerge first.
        return tools.OrderByDescending(tool => isUnityAsset && tool.Name == "UnityYAMLMerge").ToList();
    }

    /// <summary>
    /// Opens the tool and waits until it closes. If it saved a result without conflict markers,
    /// the file is marked resolved; otherwise it's left conflicted.
    /// </summary>
    public async Task<MergeToolOutcome> RunAsync(string repositoryPath, string path, MergeTool tool, CancellationToken cancellationToken = default)
    {
        List<string> arguments = ["-c", "mergetool.keepBackup=false"];

        if (tool.Command is { } command)
        {
            arguments.AddRange(["-c", $"mergetool.mygit.cmd={command}", "-c", "mergetool.mygit.trustExitCode=true", "mergetool", "--tool=mygit"]);
        }
        else
        {
            arguments.Add("mergetool");
        }

        arguments.AddRange(["--no-prompt", "--", path]);

        var result = await _git.RunAsync(repositoryPath, arguments, cancellationToken: cancellationToken);

        var fullPath = Path.Combine(repositoryPath, path.Replace('/', Path.DirectorySeparatorChar));
        if (ConflictService.HasConflictMarkers(fullPath))
        {
            // Saved with markers still in it: put the file back to "conflicted" so it isn't committed like that.
            await _git.RunAsync(repositoryPath, ["checkout", "-m", "--", path], cancellationToken: cancellationToken);
            return MergeToolOutcome.NotResolved;
        }

        var stillConflicted = await _git.RunAsync(repositoryPath, ["ls-files", "-u", "--", path], cancellationToken: cancellationToken);
        return result.Succeeded && stillConflicted.OutputLines.Count == 0 ? MergeToolOutcome.Resolved : MergeToolOutcome.NotResolved;
    }

    /// <summary>The command line git runs for a known tool (paths with forward slashes for git's shell).</summary>
    public static string CommandFor(string name, string exe)
    {
        var program = $"\"{exe.Replace('\\', '/')}\"";
        return name switch
        {
            // Unity's documented setup: merge -p base theirs ours result
            "UnityYAMLMerge" => $"{program} merge -p \"$BASE\" \"$REMOTE\" \"$LOCAL\" \"$MERGED\"",
            "VS Code" => $"{program} --wait --merge \"$REMOTE\" \"$LOCAL\" \"$BASE\" \"$MERGED\"",
            "Rider" => $"{program} merge \"$LOCAL\" \"$REMOTE\" \"$BASE\" \"$MERGED\"",
            _ => $"{program} \"$MERGED\"",
        };
    }

    private IReadOnlyList<(string Name, string Path, bool UnityOnly)> Installed()
    {
        if (_installed is not null)
        {
            return _installed;
        }

        var found = new List<(string, string, bool)>();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        if (FirstExisting(
                Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(programFiles, "Microsoft VS Code", "Code.exe")) is { } code)
        {
            found.Add(("VS Code", code, false));
        }

        if ((Newest(Path.Combine(programFiles, "JetBrains"), "JetBrains Rider*", Path.Combine("bin", "rider64.exe"))
             ?? Newest(Path.Combine(local, "Programs"), "Rider*", Path.Combine("bin", "rider64.exe"))) is { } rider)
        {
            found.Add(("Rider", rider, false));
        }

        // Unity Hub keeps one folder per editor version; use the newest.
        if ((Newest(Path.Combine(programFiles, "Unity", "Hub", "Editor"), "*", Path.Combine("Editor", "Data", "Tools", "UnityYAMLMerge.exe"))
             ?? FirstExisting(Path.Combine(programFiles, "Unity", "Editor", "Data", "Tools", "UnityYAMLMerge.exe"))) is { } unity)
        {
            found.Add(("UnityYAMLMerge", unity, true));
        }

        _installed = found;
        return found;
    }

    private static string? FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists);

    private static string? Newest(string folder, string pattern, string relativeExe)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateDirectories(folder, pattern)
            .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase)
            .Select(directory => Path.Combine(directory, relativeExe))
            .FirstOrDefault(File.Exists);
    }
}

using System.Diagnostics;

namespace MyGit.Core.Tools;

/// <summary>An installed program My Git can open a repository or file in.</summary>
/// <param name="Id">Stable name saved in settings, e.g. "vscode".</param>
/// <param name="Name">What menus show, e.g. "VS Code".</param>
public sealed record ExternalTool(string Id, string Name, string Path);

/// <summary>
/// Finds code editors and terminals on this machine, in the order "Automatic" picks them.
/// Found once per run: installing one while My Git is open needs a restart.
/// </summary>
public static class ToolLocator
{
    private static readonly Lazy<IReadOnlyList<ExternalTool>> FoundEditors = new(FindEditors);
    private static readonly Lazy<IReadOnlyList<ExternalTool>> FoundTerminals = new(FindTerminals);

    public static IReadOnlyList<ExternalTool> Editors => FoundEditors.Value;

    public static IReadOnlyList<ExternalTool> Terminals => FoundTerminals.Value;

    /// <summary>The preferred one if it's installed, else the first found ("Automatic"), else null.</summary>
    public static ExternalTool? Pick(IReadOnlyList<ExternalTool> tools, string? preferredId) =>
        tools.FirstOrDefault(tool => tool.Id == preferredId) ?? tools.FirstOrDefault();

    private static IReadOnlyList<ExternalTool> FindEditors()
    {
        if (OperatingSystem.IsMacOS())
        {
            return FindMacApps(
                ("vscode", "VS Code", "Visual Studio Code.app"),
                ("cursor", "Cursor", "Cursor.app"),
                ("rider", "Rider", "Rider.app"),
                ("zed", "Zed", "Zed.app"),
                ("sublime", "Sublime Text", "Sublime Text.app"),
                ("xcode", "Xcode", "Xcode.app"));
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var found = new List<ExternalTool>();

        Add(found, "vscode", "VS Code",
            Path.Combine(local, "Programs", "Microsoft VS Code", "Code.exe"),
            Path.Combine(programFiles, "Microsoft VS Code", "Code.exe"));
        Add(found, "cursor", "Cursor", Path.Combine(local, "Programs", "cursor", "Cursor.exe"));
        Add(found, "rider", "Rider",
            Newest(Path.Combine(programFiles, "JetBrains"), "JetBrains Rider*", Path.Combine("bin", "rider64.exe")),
            Newest(Path.Combine(local, "Programs"), "Rider*", Path.Combine("bin", "rider64.exe")));
        Add(found, "visualstudio", "Visual Studio", VisualStudio());

        return found;
    }

    private static IReadOnlyList<ExternalTool> FindTerminals()
    {
        if (OperatingSystem.IsMacOS())
        {
            return FindMacApps(
                ("iterm", "iTerm", "iTerm.app"),
                ("warp", "Warp", "Warp.app"),
                ("ghostty", "Ghostty", "Ghostty.app"),
                ("terminal", "Terminal", "Utilities/Terminal.app"));
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var found = new List<ExternalTool>();

        Add(found, "windowsterminal", "Windows Terminal", Path.Combine(local, "Microsoft", "WindowsApps", "wt.exe"));
        Add(found, "pwsh", "PowerShell 7", Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"));
        Add(found, "powershell", "Windows PowerShell", Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"));
        Add(found, "gitbash", "Git Bash", Path.Combine(programFiles, "Git", "git-bash.exe"));
        Add(found, "cmd", "Command Prompt", Path.Combine(system, "cmd.exe"));

        return found;
    }

    // On a Mac, ExternalTool.Path is the .app bundle (opened with "open -a"). Looked for in /Applications,
    // ~/Applications (where JetBrains Toolbox puts Rider) and /System/Applications (Terminal).
    private static IReadOnlyList<ExternalTool> FindMacApps(params (string Id, string Name, string Bundle)[] apps)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] folders = ["/Applications", Path.Combine(home, "Applications"), "/System/Applications"];
        return apps
            .Select(app => (app, Path: folders.Select(folder => Path.Combine(folder, app.Bundle)).FirstOrDefault(Directory.Exists)))
            .Where(found => found.Path is not null)
            .Select(found => new ExternalTool(found.app.Id, found.app.Name, found.Path!))
            .ToList();
    }

    private static void Add(List<ExternalTool> found, string id, string name, params string?[] candidates)
    {
        if (candidates.FirstOrDefault(path => path is not null && File.Exists(path)) is { } path)
        {
            found.Add(new ExternalTool(id, name, path));
        }
    }

    // vswhere ships with every Visual Studio installer and knows where the newest one is.
    private static string? VisualStudio()
    {
        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(vswhere, "-latest -property productPath")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            var output = process?.StandardOutput.ReadToEnd().Trim();
            process?.WaitForExit(5000);
            return output is { Length: > 0 } ? output : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? Newest(string folder, string pattern, string relativeExe) =>
        !Directory.Exists(folder)
            ? null
            : Directory.EnumerateDirectories(folder, pattern)
                .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase)
                .Select(directory => Path.Combine(directory, relativeExe))
                .FirstOrDefault(File.Exists);
}

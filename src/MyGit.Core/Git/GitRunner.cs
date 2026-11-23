using System.Diagnostics;
using System.Text;

namespace MyGit.Core.Git;

/// <summary>
/// Default <see cref="IGitRunner"/>: starts a git process, captures its output and waits for it.
/// </summary>
public sealed class GitRunner : IGitRunner
{
    private readonly string _gitExecutable;

    public GitRunner(string gitExecutable = "git")
    {
        _gitExecutable = gitExecutable;
    }

    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(workingDirectory, arguments, environment);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Read both streams at the same time so a full buffer on one never blocks the other.
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new GitResult(process.ExitCode, await outputTask, await errorTask);
    }

    public async Task<GitResult> RunCheckedAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(workingDirectory, arguments, environment, cancellationToken);

        if (!result.Succeeded)
        {
            throw new GitCommandException(arguments, result);
        }

        return result;
    }

    private ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo(_gitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Keep file paths readable (no octal escaping of non-ASCII names).
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.quotepath=false");

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Never let git wait for input on a console nobody can see.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the check and the kill.
        }
    }
}

using System.Diagnostics;
using System.Text;

namespace MyGit.Core.Git;

/// <summary>
/// Default <see cref="IGitRunner"/>: starts a git process, captures its output and waits for it.
/// </summary>
public sealed class GitRunner : IGitRunner
{
    private readonly string _gitExecutable;
    private readonly IGitEnvironmentProvider? _environmentProvider;

    /// <param name="environmentProvider">
    /// Adds environment variables to every git command, for example GitHub authentication.
    /// </param>
    public GitRunner(IGitEnvironmentProvider? environmentProvider = null, string gitExecutable = "git")
    {
        _environmentProvider = environmentProvider;
        _gitExecutable = gitExecutable;
    }

    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default,
        Action<string>? onProgress = null)
    {
        var startInfo = CreateStartInfo(workingDirectory, arguments, environment);

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        // Read both streams at the same time so a full buffer on one never blocks the other.
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = onProgress is null
            ? process.StandardError.ReadToEndAsync(cancellationToken)
            : ReadLinesAsync(process.StandardError, onProgress, cancellationToken);

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
        CancellationToken cancellationToken = default,
        Action<string>? onProgress = null)
    {
        var result = await RunAsync(workingDirectory, arguments, environment, cancellationToken, onProgress);

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

        AddEnvironment(startInfo, _environmentProvider?.GetEnvironment());
        AddEnvironment(startInfo, environment);

        return startInfo;
    }

    private static void AddEnvironment(ProcessStartInfo startInfo, IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null)
        {
            return;
        }

        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }
    }

    /// <summary>
    /// Reads a stream and reports each line as it arrives. git redraws progress lines
    /// with '\r', so both '\r' and '\n' end a line. Returns everything that was read.
    /// </summary>
    private static async Task<string> ReadLinesAsync(
        StreamReader reader,
        Action<string> onLine,
        CancellationToken cancellationToken)
    {
        var all = new StringBuilder();
        var line = new StringBuilder();
        var buffer = new char[1024];

        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var character = buffer[i];
                all.Append(character);

                if (character is '\r' or '\n')
                {
                    if (line.Length > 0)
                    {
                        onLine(line.ToString());
                        line.Clear();
                    }
                }
                else
                {
                    line.Append(character);
                }
            }
        }

        if (line.Length > 0)
        {
            onLine(line.ToString());
        }

        return all.ToString();
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

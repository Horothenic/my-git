namespace MyGit.Core.Git;

/// <summary>
/// The outcome of one git command: exit code plus everything it printed.
/// </summary>
public sealed record GitResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>Standard output split into non-empty lines.</summary>
    public IReadOnlyList<string> OutputLines =>
        StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                      .Select(line => line.TrimEnd('\r'))
                      .ToList();
}

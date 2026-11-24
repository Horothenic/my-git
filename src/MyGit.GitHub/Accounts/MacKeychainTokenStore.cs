using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace MyGit.GitHub.Accounts;

/// <summary>
/// Stores the token in the macOS Keychain (Keychain Access → login → "MyGit:github.com"),
/// through the system's own <c>security</c> tool. The token is written through its standard input,
/// so it never shows up in a process list.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed partial class MacKeychainTokenStore : ITokenStore
{
    private const string Security = "/usr/bin/security";

    private readonly string _service;

    public MacKeychainTokenStore(string service = "MyGit:github.com")
    {
        _service = service;
    }

    public (string Login, string Token)? Read()
    {
        // -w prints just the password; without it, the attributes (the account is the login).
        var token = Run(["find-generic-password", "-s", _service, "-w"]);
        if (token.ExitCode != 0 || token.Output.Trim().Length == 0)
        {
            return null;
        }

        var attributes = Run(["find-generic-password", "-s", _service]);
        var login = AccountPattern().Match(attributes.Output) is { Success: true } match ? match.Groups[1].Value : null;
        return login is null ? null : (login, token.Output.Trim());
    }

    public void Write(string login, string token)
    {
        // "security -i" reads commands from standard input; -U replaces an existing entry.
        var command = $"add-generic-password -U -a {Quote(login)} -s {Quote(_service)} -l {Quote("GitHub token used by My Git")} -w {Quote(token)}\n";
        var result = Run(["-i"], input: command);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Couldn't save the token in the Keychain: {result.Error.Trim()}");
        }
    }

    public void Delete() => Run(["delete-generic-password", "-s", _service]);

    // security's own command language: double quotes, with \ and " escaped.
    private static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private static (int ExitCode, string Output, string Error) Run(string[] arguments, string? input = null)
    {
        var start = new ProcessStartInfo(Security)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = input is not null,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    // e.g.     "acct"<blob>="Horothenic"
    [GeneratedRegex("\"acct\"<blob>=\"([^\"]*)\"")]
    private static partial Regex AccountPattern();
}

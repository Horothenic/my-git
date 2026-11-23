namespace MyGit.Core.Git;

/// <summary>
/// Builds environment variables that pass git config values to one git command
/// (GIT_CONFIG_COUNT / GIT_CONFIG_KEY_n / GIT_CONFIG_VALUE_n, git 2.31+).
/// Unlike "-c key=value" arguments, the values never show up in the process command line.
/// </summary>
public static class GitConfigEnvironment
{
    public static IReadOnlyDictionary<string, string> Create(IReadOnlyList<KeyValuePair<string, string>> settings)
    {
        var environment = new Dictionary<string, string>
        {
            ["GIT_CONFIG_COUNT"] = settings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        for (var i = 0; i < settings.Count; i++)
        {
            environment[$"GIT_CONFIG_KEY_{i}"] = settings[i].Key;
            environment[$"GIT_CONFIG_VALUE_{i}"] = settings[i].Value;
        }

        return environment;
    }
}

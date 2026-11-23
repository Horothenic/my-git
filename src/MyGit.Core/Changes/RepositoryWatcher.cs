namespace MyGit.Core.Changes;

/// <summary>
/// Watches a repository folder and raises <see cref="Changed"/> shortly after files change,
/// so the Unstaged and Staged lists refresh on their own (for example when Unity saves).
///
/// Bursts of changes are merged: <see cref="Changed"/> fires once things have been quiet for
/// <see cref="QuietPeriod"/>, and at most once per <see cref="MinimumInterval"/> during a long burst.
/// Inside .git only the files that affect status count (index, HEAD, refs).
/// </summary>
public sealed class RepositoryWatcher : IDisposable
{
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(2);

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private DateTime _firstPendingChange = DateTime.MaxValue;

    public RepositoryWatcher(string repositoryPath)
    {
        _timer = new Timer(_ => Fire());

        _watcher = new FileSystemWatcher(repositoryPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };

        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnFileEvent;

        // Too many changes at once (e.g. a big checkout) overflow the buffer: just refresh.
        _watcher.Error += (_, _) => Schedule();

        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Raised on a background thread. Handlers must switch to the UI thread themselves.</summary>
    public event Action? Changed;

    /// <summary>
    /// True when a change to <paramref name="relativePath"/> (relative to the repository root)
    /// could change what git status shows.
    /// </summary>
    public static bool Matters(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');

        if (!path.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) && !string.Equals(path, ".git", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Inside .git: objects, logs and lock files churn constantly but don't change status by themselves.
        var inside = path.Length > 5 ? path[5..] : string.Empty;
        return string.Equals(inside, "index", StringComparison.OrdinalIgnoreCase)
            || string.Equals(inside, "HEAD", StringComparison.OrdinalIgnoreCase)
            || inside.StartsWith("refs/", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (Matters(e.Name ?? string.Empty))
        {
            Schedule();
        }
    }

    private void Schedule()
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_firstPendingChange == DateTime.MaxValue)
            {
                _firstPendingChange = now;
            }

            // Wait for a quiet moment, but never longer than MinimumInterval after the first change.
            var deadline = _firstPendingChange + MinimumInterval;
            var due = now + QuietPeriod < deadline ? QuietPeriod : deadline - now;
            _timer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_gate)
        {
            _firstPendingChange = DateTime.MaxValue;
        }

        Changed?.Invoke();
    }
}

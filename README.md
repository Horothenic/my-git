# My Git

A Git desktop client for Windows, built with .NET 10 and Avalonia.

Focus areas: rebase and interactive rebase, a safe sync workflow, and **stacked pull requests** that stay in sync when you push.

> Work in progress, built phase by phase. See the pull requests for progress.

## Requirements

- Windows 10 or 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Git for Windows](https://git-scm.com/download/win) 2.38 or newer, on `PATH`

## Run

```powershell
dotnet run --project src/MyGit.App
```

Every local build also copies the runnable app to `app\` at the repo root (ignored by git), so you can always launch `app\MyGit.exe`. If My Git is running during a build, its files are locked: the build still succeeds but warns that `app\` wasn't updated.

## Test

```powershell
dotnet test MyGit.slnx
```

## Layout

| Project | What lives there |
|---|---|
| `src/MyGit.App` | Avalonia UI: `Views` (XAML), `ViewModels`, `Styles`, `Services` |
| `src/MyGit.Core` | Git and repository logic, no UI code. All git writes go through `GitRunner`. |
| `tests/MyGit.Core.Tests` | xUnit tests that run against real temporary git repositories |

App data lives in `%APPDATA%\MyGit` (`repositories.json`, `settings.json`). Nothing is written inside your repositories.

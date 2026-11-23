using MyGit.Core.Settings;

namespace MyGit.Core.Tests.Settings;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _folder = TempRepository.CreateEmptyFolder();

    private string FilePath => Path.Combine(_folder, "settings.json");

    [Fact]
    public void Save_PersistsAcrossInstances()
    {
        var store = new AppSettingsStore(FilePath);
        store.Settings.LastRepositoryPath = @"C:\code\my-git";
        store.Settings.Theme = "Light";
        store.Save();

        var reloaded = new AppSettingsStore(FilePath).Settings;

        Assert.Equal(@"C:\code\my-git", reloaded.LastRepositoryPath);
        Assert.Equal("Light", reloaded.Theme);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileIsMissing()
    {
        var settings = new AppSettingsStore(FilePath).Settings;

        Assert.Null(settings.LastRepositoryPath);
        Assert.Equal("Dark", settings.Theme);
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenFileIsCorrupted()
    {
        File.WriteAllText(FilePath, "{ not json");

        Assert.Equal("Dark", new AppSettingsStore(FilePath).Settings.Theme);
    }

    public void Dispose() => TempRepository.DeleteFolder(_folder);
}

namespace Ember.Core.Tests;

static class TestFiles
{
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Read(string name) => File.ReadAllText(Fixture(name));

    /// <summary>Repository root (the folder with Ember.sln), for tests that scan the sources.</summary>
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Ember.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Ember.sln not found");
        }
    }
}

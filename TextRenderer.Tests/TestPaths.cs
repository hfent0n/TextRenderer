namespace TextRenderer.Tests;

public static class TestPaths
{
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Font(string fileName) => Path.Combine(RepoRoot, "fonts", fileName);


    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TextRenderer.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find TextRenderer.sln above test output directory.");
    }
}

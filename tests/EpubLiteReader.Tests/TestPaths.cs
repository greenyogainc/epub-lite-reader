using System.IO;

namespace EpubLiteReader.Tests;

/// <summary>Locates repository-relative fixture paths without hardcoding a build layout.</summary>
internal static class TestPaths
{
    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);

    public static string RepoRoot => RepoRootLazy.Value;

    public static string SampleEpubPath => Path.Combine(RepoRoot, "tools", "fixtures", "sample.epub");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "EpubLiteReader.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate EpubLiteReader.slnx by walking up from '{AppContext.BaseDirectory}'.");
    }
}

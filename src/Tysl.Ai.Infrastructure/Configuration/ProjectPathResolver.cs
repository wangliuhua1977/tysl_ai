namespace Tysl.Ai.Infrastructure.Configuration;

public static class ProjectPathResolver
{
    private const string SolutionFileName = "Tysl.Ai.sln";

    public static string FindProjectRoot()
    {
        foreach (var root in GetSearchRoots())
        {
            var current = root;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (File.Exists(Path.Combine(current, SolutionFileName)))
                {
                    return current;
                }

                current = Directory.GetParent(current)?.FullName;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    public static string EnsureRuntimeDirectory(params string[] segments)
    {
        var path = segments.Aggregate(
            Path.Combine(FindProjectRoot(), "runtime"),
            Path.Combine);

        Directory.CreateDirectory(path);
        return path;
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        return new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory
            }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}

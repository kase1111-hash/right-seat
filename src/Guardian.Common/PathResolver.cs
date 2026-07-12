namespace Guardian.Common;

/// <summary>
/// Locates the config directory (guardian.toml + profiles/) at runtime.
/// Searches upward from both the current working directory and the
/// application base directory, so the apps work whether launched from the
/// repository root, from bin/Debug/net8.0, or from a published layout.
/// </summary>
public static class PathResolver
{
    /// <summary>
    /// Returns the first directory named "config" that contains guardian.toml
    /// or a profiles/ subdirectory, walking up from CWD and the app base
    /// directory. Returns null if none is found.
    /// </summary>
    public static string? FindConfigDirectory()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "config");
                if (File.Exists(Path.Combine(candidate, "guardian.toml")) ||
                    Directory.Exists(Path.Combine(candidate, "profiles")))
                {
                    return candidate;
                }
                dir = dir.Parent!;
            }
        }
        return null;
    }

    /// <summary>Path to guardian.toml, or null if no config directory is found.</summary>
    public static string? FindConfigFile()
    {
        var dir = FindConfigDirectory();
        return dir is null ? null : Path.Combine(dir, "guardian.toml");
    }

    /// <summary>Path to the profiles directory, or null if no config directory is found.</summary>
    public static string? FindProfilesDirectory()
    {
        var dir = FindConfigDirectory();
        return dir is null ? null : Path.Combine(dir, "profiles");
    }
}

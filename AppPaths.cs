namespace WinAffinityEnforcer;

/// <summary>Where the utility keeps its files.</summary>
public static class AppPaths
{
    /// <summary>The editable rule file, created on first run when absent.</summary>
    public static string ConfigFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "WinAffinityEnforcer", "affinity.json");

    public static string LogFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinAffinityEnforcer", "scheduler.log");
}

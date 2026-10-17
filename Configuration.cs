using System.Text.Json;

namespace WinAffinityEnforcer;

public sealed class Configuration
{
    public int PollIntervalSeconds { get; set; } = 2;
    public List<AffinityRule> Rules { get; set; } = [];

    public static Configuration Load(string path, int cpuCount)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        };
        var config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), options)
            ?? throw new InvalidDataException("Configuration cannot be null.");
        config.Validate(cpuCount);
        return config;
    }

    public void Validate(int cpuCount)
    {
        if (cpuCount is < 1 or > 64)
            throw new InvalidDataException("This version supports one processor group (up to 64 logical CPUs).");
        if (PollIntervalSeconds is < 1 or > 3600)
            throw new InvalidDataException("pollIntervalSeconds must be between 1 and 3600.");
        if (Rules is null)
            throw new InvalidDataException("rules must be an array.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in Rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.ProcessName))
                throw new InvalidDataException("Each rule requires a processName.");
            if (rule.ProcessName.IndexOfAny(['/', '\\', ':', '*', '?']) >= 0)
                throw new InvalidDataException("processName must be an executable name, not a path or pattern.");
            if (string.IsNullOrWhiteSpace(rule.BaseName) || !names.Add(rule.BaseName))
                throw new InvalidDataException($"Empty or duplicate process name: {rule.ProcessName}");
            if (rule.Cpus is null || rule.Cpus.Count == 0 || rule.Cpus.Any(cpu => cpu < 0 || cpu >= cpuCount))
                throw new InvalidDataException($"{rule.ProcessName}: cpus must contain logical CPU numbers from 0 to {cpuCount - 1}.");
            if (rule.Cpus.Distinct().Count() != rule.Cpus.Count)
                throw new InvalidDataException($"{rule.ProcessName}: duplicate CPU numbers.");
        }
    }
}

public sealed class AffinityRule
{
    public string ProcessName { get; set; } = "";
    public List<int> Cpus { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public string BaseName => ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        ? ProcessName[..^4] : ProcessName;
    [System.Text.Json.Serialization.JsonIgnore]
    public ulong Mask => Cpus.Aggregate(0UL, (mask, cpu) => mask | (1UL << cpu));
}

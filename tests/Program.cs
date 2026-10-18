using System.Diagnostics;
using WinAffinityEnforcer;

if (args.Contains("--target"))
{
    Thread.Sleep(TimeSpan.FromMinutes(2));
    return;
}

var passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine($"PASS: {description}");
    passed++;
}
void Reject(string json, string description)
{
    var file = Path.GetTempFileName();
    try
    {
        File.WriteAllText(file, json);
        var rejected = false;
        try { Configuration.Load(file, 8); }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException) { rejected = true; }
        Check(rejected, description);
    }
    finally { File.Delete(file); }
}
bool WaitFor(Func<bool> predicate)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < TimeSpan.FromSeconds(12))
    {
        if (predicate()) return true;
        Thread.Sleep(100);
    }
    return false;
}
Process Start(string exe, params string[] arguments)
{
    var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    return Process.Start(info)!;
}

var rule = new AffinityRule { ProcessName = "Example.EXE", Cpus = [0, 3, 63] };
Check(rule.BaseName == "Example", "Executable suffix normalization");
Check(rule.Mask == 0x8000000000000009UL, "64-bit affinity mask including CPU 63");
Check(new AffinityRule { ProcessName = "a", Cpus = [0], Priority = "high" }.PriorityClass == ProcessPriorityClass.High, "Priority name parses case-insensitively");
Check(new AffinityRule { ProcessName = "a", Cpus = [0] }.PriorityClass is null, "Missing priority leaves the process alone");
Check(CpuTopology.GetCpuSets().Count > 0, "CPU set enumeration returns logical processors");
Check(AppPaths.ConfigFile.Replace('\\', '/').EndsWith("/.config/WinAffinityEnforcer/affinity.json", StringComparison.OrdinalIgnoreCase), "Default config lives under ~/.config");
new Configuration().Validate(8);
Check(true, "Empty rules are safe");
Reject("null", "Reject null configuration");
Reject("{\"rules\":null}", "Reject null rules");
Reject("{\"rules\":[null]}", "Reject null rule");
Reject("{\"pollIntervalSeconds\":0}", "Reject busy-loop polling interval");
Reject("{\"rules\":[{\"processName\":\"a\",\"cpus\":[]}]}", "Reject empty CPU set");
Reject("{\"rules\":[{\"processName\":\"a\",\"cpus\":[8]}]}", "Reject unavailable CPU");
Reject("{\"rules\":[{\"processName\":\"a\",\"cpus\":[-1]}]}", "Reject negative CPU");
Reject("{\"rules\":[{\"processName\":\"a\",\"cpus\":[1,1]}]}", "Reject duplicate CPUs");
Reject("{\"rules\":[{\"processName\":\"a\",\"cpus\":[0]},{\"processName\":\"A.exe\",\"cpus\":[1]}]}", "Reject conflicting process rules");
Reject("{\"rules\":[{\"processName\":\"C:/a.exe\",\"cpus\":[0]}]}", "Reject executable paths");
Reject("{\"rulez\":[]}", "Reject misspelled settings");
Reject("{\"rules\":[{\"processName\":\"a\",\"cpus\":[0],\"priority\":\"Faster\"}]}", "Reject unknown priority");
Reject("{\"rules\":[{\"processName\":\"a\",\"cpus\":[0],\"priority\":\"2\"}]}", "Reject numeric priority");

// Integration tests only target this test executable, never unrelated user processes.
var directory = Path.Combine(Path.GetTempPath(), "WinAffinityEnforcer-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var configPath = Path.Combine(directory, "rules.json");
var schedulerExe = Path.Combine(AppContext.BaseDirectory, "WinAffinityEnforcer.exe");
var targetExe = Environment.ProcessPath!;
Process? target = null;
Process? scheduler = null;
using var self = Process.GetCurrentProcess();
var originalAffinity = self.ProcessorAffinity;
try
{
    var available = unchecked((ulong)(long)originalAffinity);
    var firstCpu = Enumerable.Range(0, 64).First(cpu => (available & (1UL << cpu)) != 0);
    var desired = unchecked((nint)(1UL << firstCpu));
    string Config(int cpu, string? priority = null) => System.Text.Json.JsonSerializer.Serialize(new
    {
        pollIntervalSeconds = 1,
        rules = new[] { new { processName = Path.GetFileName(targetExe), cpus = new[] { cpu }, priority } }
    });
    File.WriteAllText(configPath, Config(firstCpu));
    target = Start(targetExe, "--target");
    scheduler = Start(schedulerExe, "--config", configPath);
    Check(WaitFor(() => { target.Refresh(); return target.ProcessorAffinity == desired; }), "Apply affinity to existing process");
    Check(!scheduler.HasExited, "Background scheduler remains running");
    using (var duplicate = Start(schedulerExe, "--config", configPath))
        Check(duplicate.WaitForExit(5000) && duplicate.ExitCode == 0, "Duplicate instance exits cleanly");

    File.WriteAllText(configPath, Config(firstCpu, "High"));
    Check(WaitFor(() => { target.Refresh(); return target.PriorityClass == ProcessPriorityClass.High; }), "Apply process priority");
    File.WriteAllText(configPath, Config(firstCpu));

    File.WriteAllText(configPath, "{\"pollIntervalSeconds\":0}");
    Thread.Sleep(1800);
    Check(!scheduler.HasExited, "Invalid reload retains valid configuration");
    File.WriteAllText(configPath, Config(firstCpu));

    if (System.Numerics.BitOperations.PopCount(available) > 1)
    {
        target.ProcessorAffinity = unchecked((nint)available);
        Check(WaitFor(() => { target.Refresh(); return target.ProcessorAffinity == desired; }), "Reapply externally changed affinity");
        var secondCpu = Enumerable.Range(0, 64).First(cpu => cpu != firstCpu && (available & (1UL << cpu)) != 0);
        File.WriteAllText(configPath, Config(secondCpu));
        var secondMask = unchecked((nint)(1UL << secondCpu));
        Check(WaitFor(() => { target.Refresh(); return target.ProcessorAffinity == secondMask; }), "Hot reload valid rules");
    }

    target.Kill();
    target.WaitForExit();
    target.Dispose();
    target = Start(targetExe, "--target");
    // Restore the original test rule and verify discovery of a newly started target.
    File.WriteAllText(configPath, Config(firstCpu));
    Check(WaitFor(() => { target.Refresh(); return target.ProcessorAffinity == desired; }), "Apply affinity after target restart");
    using var stop = Start(schedulerExe, "--config", configPath, "--stop");
    Check(stop.WaitForExit(5000) && scheduler.WaitForExit(5000) && scheduler.ExitCode == 0, "Graceful background stop");
}
finally
{
    // A name-based rule also matches this runner; restore its original affinity on exit.
    if (scheduler is not null)
    {
        if (!scheduler.HasExited) { scheduler.Kill(); scheduler.WaitForExit(); }
        scheduler.Dispose();
    }
    if (target is not null)
    {
        if (!target.HasExited) { target.Kill(); target.WaitForExit(); }
        target.Dispose();
    }
    self.ProcessorAffinity = originalAffinity;
    File.Delete(configPath);
    Directory.Delete(directory);
}
Console.WriteLine($"{passed} tests passed.");

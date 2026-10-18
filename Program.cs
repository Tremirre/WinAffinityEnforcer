using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace WinAffinityEnforcer;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern ushort GetActiveProcessorGroupCount();

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

    [STAThread]
    private static int Main(string[] args)
    {
        var log = new AppLog();
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "affinity.json");
            var stop = false;
            var validate = false;
            var listCpus = false;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--config" when i + 1 < args.Length:
                        configPath = Path.GetFullPath(args[++i]);
                        break;
                    case "--stop": stop = true; break;
                    case "--validate": validate = true; break;
                    case "--list-cpus": listCpus = true; break;
                    default: throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
                }
            }

            if (listCpus)
            {
                // No console window exists, so this is also written to the log; stdout works when redirected.
                var topology = CpuTopology.Describe();
                log.Write($"CPU topology:{Environment.NewLine}{topology}");
                Console.WriteLine(topology);
                return 0;
            }

            // Separate configurations can run independently; the same one cannot run twice.
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configPath.ToUpperInvariant())))[..24];
            var eventName = $@"Local\WinAffinityEnforcer.Stop.{key}";
            if (stop)
            {
                try
                {
                    using var existing = EventWaitHandle.OpenExisting(eventName);
                    existing.Set();
                }
                catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }

            if (GetActiveProcessorGroupCount() != 1)
                throw new NotSupportedException("Multiple processor groups detected. This version only supports systems with one group (up to 64 logical CPUs).");
            var cpuCount = checked((int)GetActiveProcessorCount(0));
            var config = Configuration.Load(configPath, cpuCount);
            if (validate)
            {
                log.Write($"Configuration valid: {configPath} ({config.Rules.Count} rules, {cpuCount} logical CPUs).");
                return 0;
            }

            using var mutex = new Mutex(true, $@"Local\WinAffinityEnforcer.Instance.{key}", out var isNew);
            if (!isNew) return 0;
            using var stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
            log.Write($"Started. Configuration: {configPath}; {cpuCount} logical CPUs.");
            // Read the initial file again in the loop: even an edit during startup is noticed.
            string? lastContents = null;
            var failures = new Dictionary<string, DateTime>();
            while (!stopEvent.WaitOne(0))
            {
                try
                {
                    var contents = File.ReadAllText(configPath);
                    if (contents != lastContents)
                    {
                        // Do not replace valid rules if an editor has temporarily written invalid JSON.
                        config = Configuration.Load(configPath, cpuCount);
                        lastContents = contents;
                        failures.Clear();
                        log.Write($"Loaded {config.Rules.Count} rule(s).");
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    LogThrottled(failures, "config", $"Cannot reload configuration; keeping previous rules. {ex.Message}", log);
                }

                Apply(config, failures, log);
                if (stopEvent.WaitOne(TimeSpan.FromSeconds(config.PollIntervalSeconds))) break;
            }
            log.Write("Stopped. Existing process affinities are left unchanged.");
            return 0;
        }
        catch (Exception ex)
        {
            log.Write($"Fatal: {ex.Message}");
            return 1;
        }
    }

    private static void Apply(Configuration config, Dictionary<string, DateTime> failures, AppLog log)
    {
        foreach (var rule in config.Rules)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(rule.BaseName); }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                LogThrottled(failures, rule.BaseName, $"Cannot find {rule.ProcessName}: {ex.Message}", log);
                continue;
            }
            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        var desired = unchecked((nint)rule.Mask);
                        if (process.ProcessorAffinity != desired)
                            process.ProcessorAffinity = desired;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        LogThrottled(failures, rule.BaseName, $"Cannot set affinity for {rule.ProcessName}: {ex.Message} Elevated processes may require running this utility as administrator.", log);
                    }
                }
            }
        }
    }

    private static void LogThrottled(Dictionary<string, DateTime> failures, string key, string message, AppLog log)
    {
        var now = DateTime.UtcNow;
        if (!failures.TryGetValue(key, out var last) || now - last >= TimeSpan.FromMinutes(5))
        {
            failures[key] = now;
            log.Write(message);
        }
    }
}

internal sealed class AppLog
{
    private readonly string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinAffinityEnforcer", "scheduler.log");

    public void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 1_048_576)
                File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must not stop affinity enforcement (e.g. when the disk is full).
        }
    }
}

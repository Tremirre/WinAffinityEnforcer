# WinAffinityEnforcer

A small, ad-free, background-only Windows CPU affinity enforcer. No window, tray icon, telemetry, service, or external packages. Matches all instances of an executable by name and periodically reapplies affinity, including after a process restarts.

## Build

Requires the .NET 10 SDK on Windows x64:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The resulting `publish` folder is portable; the target computer does not need .NET installed. Keep the EXE, `affinity.json`, and `scripts` folder together. The app is not automatically started or registered during build.

## Configure

Edit `affinity.json` beside the published EXE. Initially it has **no rules**, so nothing is changed. For example:

```json
{
  "pollIntervalSeconds": 2,
  "rules": [
    { "processName": "example.exe", "cpus": [0, 1, 2, 3], "priority": "High" }
  ]
}
```

Replace `example.exe` with the executable name shown in Task Manager's Details tab. CPU numbers are **zero-based logical processors**, not physical cores. Do not assume particular numbers mean P-cores/E-cores; see the next section. `.exe` is optional; matching is case-insensitive. Each name may appear only once; paths and wildcards are not supported.

`priority` is optional. Omit it and the process priority is left alone. Accepted values, case-insensitive: `Idle`, `BelowNormal`, `Normal`, `AboveNormal`, `High`, `RealTime`. `RealTime` is strongly discouraged: it can starve the entire system, including this utility, leaving you with no choice but a hard reset. `High` is normally the highest value worth using for a game. Raising another process to `High` or `RealTime` can require running this utility as administrator. As with affinity, priority is enforced while the rule exists but is **not undone** when you remove the rule or stop the utility.

Double-click `WinAffinityEnforcer.exe` to start it silently. It reads the JSON every polling interval, applies rules to existing processes, and catches new processes on the next poll. Short-lived processes can exit before that poll. Invalid edits keep the last valid configuration; an invalid configuration at startup prevents the app from starting. Errors are logged to `%LOCALAPPDATA%\WinAffinityEnforcer\scheduler.log`, with one rotated backup.

Only one instance per configuration path can run in each Windows session. Optional arguments: `--config "C:\path\rules.json"`, `--validate` (exit code 0 if valid, 1 otherwise; results go to the log), `--list-cpus` (prints the CPU layout; results go to the log and to standard output), and `--stop`. When using a custom configuration, pass the same `--config` path with `--stop`.

## Hybrid CPUs (P-cores and E-cores)

On Intel hybrid CPUs (12th generation and newer), logical CPU numbers do **not** put the P-cores first, and the layout differs from machine to machine, so never copy CPU numbers from a forum post.

Run `WinAffinityEnforcer.exe --list-cpus` to print the real layout. The app has no console window, so redirect the output or read the log:

```powershell
.\WinAffinityEnforcer.exe --list-cpus > cpus.txt
Get-Content "$env:LOCALAPPDATA\WinAffinityEnforcer\scheduler.log" -Tail 5
```

Windows gives the faster P-cores the **higher** `EfficiencyClass` and the E-cores the lower one. For example, an Intel Core Ultra 9 285K (8 P-cores, 16 E-cores, no Hyper-Threading) reports:

```text
Logical CPUs: 24
P-cores (fastest, EfficiencyClass 1): 0, 1, 10, 11, 12, 13, 22, 23
E-cores (EfficiencyClass 0): 2, 3, 4, 5, 6, 7, 8, 9, 14, 15, 16, 17, 18, 19, 20, 21
```

The `affinity.json` in this repository is tuned for that machine: `rome2.exe` (Total War: Rome II) is limited to all eight P-cores and raised to `High` priority.

## Start with Windows / stop

Run these in PowerShell from the published folder:

```powershell
.\scripts\Enable-Startup.ps1
.\scripts\Stop.ps1
.\scripts\Disable-Startup.ps1
```

Startup is opt-in, current-user only, and happens **at sign-in**, not before anyone logs in. The startup script registers the EXE in the current user's `Run` registry key; it does not launch it immediately. Run it again after moving the folder. The scripts use the default configuration beside the EXE. Your PowerShell execution policy may require explicitly allowing the local scripts.

Stopping or removing a rule does **not** undo affinity already applied to a running process. Restart that process or restore its affinity in Task Manager.

## Limitations

- Windows x64, one processor group, at most 64 logical CPUs. Multi-group machines are explicitly rejected rather than silently applying the wrong mask.
- Changes whole-process affinity and process priority class only. No CPU Sets, thread-level affinity, I/O priority, or power plans.
- Elevated processes generally require launching this utility as administrator. The optional sign-in entry does not elevate it. Protected processes can reject affinity changes even with administrator rights.
- Rules apply to every accessible process with the matching executable name; no path/user filtering. Children with different names need their own rules (Windows may also inherit affinity on process creation).
- Enforcement is periodic, not instantaneous. Other programs changing affinity can conflict with these rules.

## Tests

```powershell
dotnet run --project tests/WinAffinityEnforcer.Tests.csproj
```

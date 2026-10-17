# ColigationScheduler

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
    { "processName": "example.exe", "cpus": [0, 1, 2, 3] }
  ]
}
```

Replace `example.exe` with the executable name shown in Task Manager's Details tab. CPU numbers are **zero-based logical processors**, not physical cores. Do not assume particular numbers mean P-cores/E-cores. `.exe` is optional; matching is case-insensitive. Each name may appear only once; paths and wildcards are not supported.

Double-click `ColigationScheduler.exe` to start it silently. It reads the JSON every polling interval, applies rules to existing processes, and catches new processes on the next poll. Short-lived processes can exit before that poll. Invalid edits keep the last valid configuration; an invalid configuration at startup prevents the app from starting. Errors are logged to `%LOCALAPPDATA%\ColigationScheduler\scheduler.log`, with one rotated backup.

Only one instance per configuration path can run in each Windows session. Optional arguments: `--config "C:\path\rules.json"`, `--validate` (exit code 0 if valid, 1 otherwise; results in log), and `--stop`. When using a custom configuration, pass the same `--config` path with `--stop`.

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
- Uses hard process affinity, not CPU Sets or thread-specific rules. No priorities or power plans are changed.
- Elevated processes generally require launching this utility as administrator. The optional sign-in entry does not elevate it. Protected processes can reject affinity changes even with administrator rights.
- Rules apply to every accessible process with the matching executable name; no path/user filtering. Children with different names need their own rules (Windows may also inherit affinity on process creation).
- Enforcement is periodic, not instantaneous. Other programs changing affinity can conflict with these rules.

## Tests

```powershell
dotnet run --project tests/ColigationScheduler.Tests.csproj
```

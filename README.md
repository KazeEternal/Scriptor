# Scriptor

`Scriptor` is a .NET 10 script runtime with both a desktop GUI (Avalonia) and a CLI host for running user-authored C# routines.

It is designed for **hot-loaded automation scripts**: drop/update `.cs` files in `User_Defined_Scripts`, reload, and run routines immediately.

## What this project does

- Discovers script collections/routines from C# source files.
- Dynamically compiles and runs routines at runtime.
- Supports script metadata via attributes for names, descriptions, parameters, and package dependencies.
- Provides rich execution telemetry (log rows, progress bars, task-level details, success/failure state).
- Persists useful runtime state (defaults, playlists, diagnostics, session logs, window layout).

## Primary components

- `ScriptorCommon`  
  Core runtime, dynamic compilation, metadata models, logger, progress-channel API, and script project generation.

- `GUI`  
  Avalonia desktop app for browsing routines, configuring parameters, running routines/playlists, and viewing progress/log output.

- `Scripts`  
  Console host for interactive script execution.

- `User_Defined_Scripts`  
  Hot-loaded script source folder (your automation routines live here).

## How it is used

### 1) Author scripts
Create classes implementing `IScriptCollection` in `User_Defined_Scripts` and annotate with:

- `[ScriptCollectionName]`
- `[ScriptCollectionDescription]`
- `[ScriptRoutine]`
- `[Parameter]`
- optional `[ScriptPackageDependency]`

### 2) Run from GUI
- Start the `GUI` project.
- Select routines in the left tree.
- Configure parameters in `Routine Configuration`.
- Run individual routines or playlists.
- Use `Reload Scripts` after edits.

### 3) Run from CLI
The `Scripts` project supports both interactive mode and CI-friendly non-interactive mode.

#### Show CLI help
```powershell
dotnet run --project Scripts -- --help
```

#### Discovery commands
```powershell
dotnet run --project Scripts -- --list-routines
dotnet run --project Scripts -- --list-playlists
dotnet run --project Scripts -- --list-playlists --playlist-file ".\User_Defined_Scripts\.scriptor\playlists.json"
```

#### Run a routine from CLI (Jenkins/GitLab CI friendly)
```powershell
# by routine display name with explicit parameter override(s)
dotnet run --project Scripts -- --run-routine "Hello to a Person" --set "Person Name=CI Runner"

# by stable routine id with json parameter file
dotnet run --project Scripts -- --run-routine "Scripts.Scripting.HelloWorldScripts.HelloWorlder" --params-file ".\ci\params.json"
```

Parameter JSON shape:
```json
{
  "Person Name": "CI Runner"
}
```

#### Run a playlist from CLI
```powershell
dotnet run --project Scripts -- --run-playlist "Nightly Build Playlist"
dotnet run --project Scripts -- --run-playlist "Nightly Build Playlist" --playlist-file ".\User_Defined_Scripts\.scriptor\playlists.json"
```

#### Common options
- `--scripts-root <path>` override scripts root folder.
- `--playlist-file <path>` override playlist file location.
- `--set "Key=Value"` repeatable per-parameter override.
- `--params-file <path>` load parameter overrides from JSON.

## Key features

### Hot reload and dynamic execution
- Watches script files and recompiles on change.
- Excludes only failing script files when possible, so other scripts can still run.

### Rich parameter editing
The GUI automatically picks editors by parameter type and usage hints:

- `bool` -> checkbox
- `enum` -> dropdown
- numeric types -> numeric input
- `FileInfo` -> file picker
- `DirectoryInfo` -> folder picker
- usage hints:
  - `ui:file`
  - `ui:folder` / `ui:directory`
  - `ui:password`
  - `ui:multiline`
  - `ui:slider(min,max,step)`

  ### Quick command
  Keep the GUI running in the background or minimized, then press **Windows+Alt+S** to open a command palette centered on the display containing the mouse pointer. Search routines and press Enter to run them with their suggested default parameters, edit `Parameter=Value` overrides inline, or use `>reload`, `>show`, and `>minimize`.
  The selected routine also shows its script and parameter descriptions from the routine attributes.

### Progress channels and task-level logs
- `context.CreateProgressChannel(...)` for managed progress keys.
- `Report(...)` for progress updates.
- `LogInfo/LogWarning/LogError(...)` for task-scoped nested output.

### Playlists
- Build playlists from routines.
- Execute sequentially or via parallel groups.
- Use **Edit Playlists** in the GUI to create, rename, reorder, and delete playlists; add routines, configure parallel groups, and change the per-playlist routine parameters by selecting the routine in the playlist tree.
- Right-click a source routine to add it to a playlist or create a playlist for it. The **Add to Playlist** submenu lists most-recently edited playlists first. Right-click playlist entries to edit, remove, or refresh them, and drag a playlist routine onto a sibling to change its execution order.
- Playlist item logs can collapse automatically on completion.
- Can be executed from CLI (`--run-playlist`) for CI/CD scenarios.

### Logging and diagnostics
- Session log files are created per app session and flushed on every write.
- Unhandled/unobserved exceptions are logged.
- Runtime compile diagnostics are persisted for troubleshooting.

### Script package dependencies
- Use `[ScriptPackageDependency("Package.Id", "Version")]` in scripts.
- Runtime detects package dependencies, restores generated script project packages, and loads assemblies for dynamic compile/run.

## Project data/state files
Under `<ScriptsRoot>\\.scriptor` (typically `User_Defined_Scripts\\.scriptor`):

- `defaults.json` - saved parameter defaults
- `playlists.json` - playlists
- `window-state.json` - GUI size/position/state
- `last-diagnostics.txt` - latest compile diagnostics
- `logs\\session-*.log` - session runtime logs
- `CompiledScripts\\` - generated runtime assemblies

## Notes

- Current target framework is `.NET 10`.
- Some scripts depend on external tools/services (for example Maven, SSH, network shares, remote Linux commands).
- For package-based scripts, keep package IDs/versions explicit and valid (NuGet IDs are case-insensitive, but conventional casing is recommended).

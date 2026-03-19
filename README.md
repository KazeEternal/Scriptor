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
Start the `Scripts` project for console-driven routine selection/execution.

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

### Progress channels and task-level logs
- `context.CreateProgressChannel(...)` for managed progress keys.
- `Report(...)` for progress updates.
- `LogInfo/LogWarning/LogError(...)` for task-scoped nested output.

### Playlists
- Build playlists from routines.
- Execute sequentially or via parallel groups.
- Playlist item logs can collapse automatically on completion.

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

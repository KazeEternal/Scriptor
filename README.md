# Scriptor

`Scriptor` is a .NET 10 script runtime with both a desktop GUI (Avalonia) and a CLI host for running user-authored C# routines.

It is designed for **hot-loaded automation scripts**: drop/update `.cs` files in `User_Defined_Scripts`, reload, and run routines immediately.

## What this project does

- Discovers script collections/routines from C# source files.
- Dynamically compiles and runs routines at runtime.
- Supports script metadata via attributes for names, descriptions, parameters, and package dependencies.
- Provides rich execution telemetry (log rows, progress bars, task-level details, success/failure state).
- Persists useful runtime state (defaults, playlists, commands, diagnostics, session logs, window layout).

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
- Select routines in the **Collections** tree, configure parameters in **Routine Configuration**, then run them.
- Use **PlayLists** to run saved sequential or parallel routine groups.
- Use **Commands** for lightweight browser and program launches.
- Right-click tree entries for contextual actions. Commands can also be double-clicked to run.
- Use **Reload Scripts** after edits; script and command changes are also detected automatically.

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
Keep the GUI running in the background or minimized, then press **Windows+Alt+S** to open a command palette centered on the display containing the mouse pointer.

- Type part of a command or routine name; commands are listed before routines.
- Press **Tab** to accept the top match. Suggested parameter assignments are selected for quick editing.
- Press **Enter** to run the selected item, or **Escape** to dismiss the palette.
- Override script parameters inline: `Routine Name -- Parameter=Value; Other Parameter=Value`.
- Run built-in palette commands: `>reload`, `>show`, and `>minimize`.

The selected routine shows its script and parameter descriptions from its attributes.

### Commands
The **Commands** category is for lightweight URL and program launches. Commands are stored in `<ScriptsRoot>\.scriptor\commands.json` and hot-reload after file changes. New script roots receive **Open Pond.net** and **Open MakeMKV** automatically.

- URL commands open HTTP(S) links in the default browser and cache the site's `/favicon.ico` when available.
- Program commands launch a local executable with optional arguments.
- Commands use the bundled Scriptor image when no custom/site icon is available.
- Set `iconPath` to a PNG, ICO, or Avalonia resource URI to override an icon.
- Default commands may be renamed; Scriptor identifies them by action rather than display name.

```json
[
  {
    "name": "Pond.net",
    "description": "Open the Pond.net website in your default browser.",
    "type": "Url",
    "target": "https://pond.net"
  },
  {
    "name": "MakeMKV",
    "description": "Start MakeMKV from its standard Windows installation path.",
    "type": "Program",
    "target": "C:\\Program Files (x86)\\MakeMKV\\makemkv.exe",
    "arguments": "",
    "iconPath": "C:\\path\\to\\makemkv.png"
  }
]
```

### Progress channels and task-level logs
- `context.CreateProgressChannel(...)` for managed progress keys.
- `Report(...)` for progress updates.
- `LogInfo/LogWarning/LogError(...)` for task-scoped nested output.
- Use **Copy Run Log**, or right-click the log list, to copy the current run log as timestamped plain text.

### Playlists
- Build playlists from routines.
- Execute sequentially or via parallel groups.
- Use **Edit Playlists** in the GUI to create, rename, reorder, and delete playlists; add routines, configure parallel groups, and change per-playlist routine parameters by selecting the routine in the playlist tree.
- Right-click a source routine to add it to a playlist or create a playlist for it. The **Add to Playlist** submenu lists most-recently edited playlists first.
- Right-click playlist entries to edit, remove, or refresh them. Drag a routine onto a sibling to change execution order within its sequential list or parallel group.
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
- `commands.json` - lightweight URL and program commands
- `window-state.json` - GUI size/position/state
- `last-diagnostics.txt` - latest compile diagnostics
- `logs\\session-*.log` - session runtime logs
- `CompiledScripts\\` - generated runtime assemblies

## Notes

- Current target framework is `.NET 10`.
- Some scripts depend on external tools/services (for example Maven, SSH, network shares, remote Linux commands).
- For package-based scripts, keep package IDs/versions explicit and valid (NuGet IDs are case-insensitive, but conventional casing is recommended).

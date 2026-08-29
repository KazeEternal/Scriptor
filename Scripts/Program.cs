using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Scripts.Scriptor;
using Scripts.Scriptor.Conductor;

namespace Scripts
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Logger.Event += Logger_Event;
            Logger.Warning += Logger_Warning;
            Logger.Error += Logger_Error;

            var options = ParseArgs(args);
            if (options.ShowHelp)
            {
                PrintHelp();
                return 0;
            }

            var scriptsRoot = ResolveScriptsRoot(options);
            Logger.ConfigureFileLogging(scriptsRoot);
            using var runtime = new ScriptRuntimeService(scriptsRoot);
            runtime.ScriptsReloaded += (_, snapshot) =>
            {
                Logger.WriteLine(Logger.LogLevel.Event, "Loaded {0} script collections.", snapshot.Collections.Count);
            };
            runtime.CompilationFailed += (_, diagnostics) =>
            {
                foreach (var diagnostic in diagnostics)
                {
                    Logger.WriteLine(Logger.LogLevel.Error, "{0}: {1} ({2}:{3})", diagnostic.Id, diagnostic.Message, diagnostic.Line, diagnostic.Column);
                }
            };

            runtime.StartWatching();
            runtime.ReloadScripts();

            if (args.Any(arg => arg.Equals("--gen-project", StringComparison.OrdinalIgnoreCase) ||
                                arg.Equals("--generate-project", StringComparison.OrdinalIgnoreCase)))
            {
                var commonProjectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ScriptorCommon", "ScriptorCommon.csproj"));
                if (!File.Exists(commonProjectPath))
                {
                    commonProjectPath = null;
                }

                var commonAssemblyPath = typeof(Logger).Assembly.Location;
                var snapshot = runtime.CurrentSnapshot ?? new ScriptRuntimeSnapshot(Array.Empty<ScriptCollectionDescriptor>(), Array.Empty<ScriptPackageDependency>());
                var result = ScriptProjectGenerator.EnsureScriptProject(
                    scriptsRoot,
                    snapshot.PackageDependencies,
                    commonProjectPath,
                    commonAssemblyPath);

                foreach (var message in result.Messages)
                {
                    Logger.WriteLine(Logger.LogLevel.Event, message);
                }

                return result.Succeeded ? 0 : 1;
            }

            if (options.ListRoutines)
            {
                ListRoutines(runtime.CurrentSnapshot);
                return 0;
            }

            if (options.ListPlaylists)
            {
                ListPlaylists(options, runtime.ScriptsRoot);
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(options.RunPlaylist) || !string.IsNullOrWhiteSpace(options.RunRoutine))
            {
                var nonInteractiveResult = await RunNonInteractiveAsync(runtime, scriptsRoot, options).ConfigureAwait(false);
                return nonInteractiveResult ? 0 : 1;
            }

            bool isRunning = true;
            while (isRunning)
            {
                var snapshot = runtime.CurrentSnapshot;
                if (snapshot == null || snapshot.Collections.Count == 0)
                {
                    Logger.WriteLine(Logger.LogLevel.Warning, "No scripts found in {0}", scriptsRoot);
                    Console.WriteLine("Press ENTER to retry or type 'q' to quit.");
                    if (string.Equals(Console.ReadLine(), "q", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    runtime.ReloadScripts();
                    continue;
                }

                Console.WriteLine("Select a script collection:");
                for (var i = 0; i < snapshot.Collections.Count; i++)
                {
                    var collection = snapshot.Collections[i];
                    Console.WriteLine("{0}) {1} - {2}", i + 1, collection.Name, collection.Description ?? string.Empty);
                }

                if (!TryReadSelection(snapshot.Collections.Count, out var collectionIndex))
                {
                    continue;
                }

                var selectedCollection = snapshot.Collections[collectionIndex];
                Console.Clear();
                Console.WriteLine("Select a script routine from {0}:", selectedCollection.Name);

                for (var i = 0; i < selectedCollection.Routines.Count; i++)
                {
                    var routine = selectedCollection.Routines[i];
                    Console.WriteLine("{0}) {1}", i + 1, routine.Name);
                    if (!string.IsNullOrWhiteSpace(routine.Description))
                    {
                        Console.WriteLine("    {0}", routine.Description);
                    }
                }

                if (!TryReadSelection(selectedCollection.Routines.Count, out var routineIndex))
                {
                    continue;
                }

                var selectedRoutine = selectedCollection.Routines[routineIndex];
                var arguments = new List<object?>();

                Console.Clear();
                Console.WriteLine("--- Setup for {0} ---", selectedRoutine.Name);
                foreach (var parameter in selectedRoutine.Parameters)
                {
                    var displayName = parameter.DisplayName ?? parameter.Name;
                    var defaultText = parameter.DefaultValue?.ToString();
                    var prompt = defaultText == null
                        ? $"->{displayName}: "
                        : $"->{displayName} ({defaultText}): ";

                    if (!TryReadParameterValue(parameter.ParameterType, prompt, defaultText, out var value))
                    {
                        Console.WriteLine("Invalid value for {0}", parameter.ParameterType.Name);
                        arguments.Clear();
                        break;
                    }

                    arguments.Add(value);
                }

                if (arguments.Count != selectedRoutine.Parameters.Count)
                {
                    continue;
                }

                var result = await runtime.ExecuteRoutineAsync(selectedRoutine, arguments).ConfigureAwait(false);
                Console.Write("Completed {0}: ", result.Context.Name);
                Console.ForegroundColor = result.IsSuccess ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine(result.IsSuccess ? "[SUCCESS]" : "[FAILED]");
                Console.ResetColor();

                if (result.Exception != null)
                {
                    Logger.WriteLine(Logger.LogLevel.Error, "{0}", result.Exception);
                }

                Console.WriteLine("Start Over (Y/n)? ");
                var response = Console.ReadLine();
                if (!string.IsNullOrEmpty(response) && response.Equals("n", StringComparison.OrdinalIgnoreCase))
                {
                    isRunning = false;
                }

                Console.Clear();
            }

            return 0;
        }

        private static async Task<bool> RunNonInteractiveAsync(ScriptRuntimeService runtime, string scriptsRoot, CliOptions options)
        {
            var snapshot = runtime.CurrentSnapshot;
            if (snapshot == null || snapshot.Collections.Count == 0)
            {
                Logger.WriteLine(Logger.LogLevel.Error, "No scripts found in {0}", scriptsRoot);
                return false;
            }

            var routinesById = snapshot.Collections
                .SelectMany(c => c.Routines)
                .ToDictionary(GetRoutineKey, StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(options.RunPlaylist))
            {
                return await RunPlaylistAsync(runtime, options, routinesById).ConfigureAwait(false);
            }

            return await RunRoutineAsync(runtime, options, routinesById.Values.ToList()).ConfigureAwait(false);
        }

        private static void ListRoutines(ScriptRuntimeSnapshot? snapshot)
        {
            if (snapshot == null || snapshot.Collections.Count == 0)
            {
                Logger.WriteLine(Logger.LogLevel.Warning, "No script collections found.");
                return;
            }

            foreach (var collection in snapshot.Collections)
            {
                Console.WriteLine($"[{collection.Name}]");
                if (!string.IsNullOrWhiteSpace(collection.Description))
                {
                    Console.WriteLine($"  {collection.Description}");
                }

                foreach (var routine in collection.Routines)
                {
                    Console.WriteLine($"  - {routine.Name}");
                    Console.WriteLine($"      Id: {GetRoutineKey(routine)}");
                    if (!string.IsNullOrWhiteSpace(routine.Description))
                    {
                        Console.WriteLine($"      Description: {routine.Description}");
                    }
                }

                Console.WriteLine();
            }
        }

        private static void ListPlaylists(CliOptions options, string scriptsRoot)
        {
            var playlistPath = string.IsNullOrWhiteSpace(options.PlaylistFile)
                ? Path.Combine(scriptsRoot, ".scriptor", "playlists.json")
                : Path.GetFullPath(options.PlaylistFile);

            if (!File.Exists(playlistPath))
            {
                Logger.WriteLine(Logger.LogLevel.Warning, "Playlist file not found: {0}", playlistPath);
                return;
            }

            var playlists = LoadPlaylists(playlistPath);
            if (playlists.Count == 0)
            {
                Logger.WriteLine(Logger.LogLevel.Warning, "No playlists found in {0}", playlistPath);
                return;
            }

            Console.WriteLine($"Playlists file: {playlistPath}");
            foreach (var playlist in playlists)
            {
                Console.WriteLine($"- {playlist.Name} ({playlist.Items.Count} items)");
            }
        }

        private static async Task<bool> RunPlaylistAsync(
            ScriptRuntimeService runtime,
            CliOptions options,
            IReadOnlyDictionary<string, ScriptRoutineDescriptor> routinesById)
        {
            var playlistPath = string.IsNullOrWhiteSpace(options.PlaylistFile)
                ? Path.Combine(runtime.ScriptsRoot, ".scriptor", "playlists.json")
                : Path.GetFullPath(options.PlaylistFile);

            if (!File.Exists(playlistPath))
            {
                Logger.WriteLine(Logger.LogLevel.Error, "Playlist file not found: {0}", playlistPath);
                return false;
            }

            var playlists = LoadPlaylists(playlistPath);
            var playlist = playlists.FirstOrDefault(p => string.Equals(p.Name, options.RunPlaylist, StringComparison.OrdinalIgnoreCase));
            if (playlist == null)
            {
                Logger.WriteLine(Logger.LogLevel.Error, "Playlist '{0}' not found in {1}", options.RunPlaylist, playlistPath);
                return false;
            }

            Logger.WriteLine(Logger.LogLevel.Event, "Running playlist: {0}", playlist.Name);
            var success = true;
            foreach (var item in playlist.Items)
            {
                if (item.Type == PlaylistItemType.ParallelGroup)
                {
                    var tasks = item.Children
                        .Where(c => c.Type == PlaylistItemType.Routine)
                        .Select(child => ExecutePlaylistRoutineItemAsync(runtime, child, routinesById));

                    var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                    success &= results.All(r => r);
                }
                else
                {
                    var itemSuccess = await ExecutePlaylistRoutineItemAsync(runtime, item, routinesById).ConfigureAwait(false);
                    success &= itemSuccess;
                }
            }

            return success;
        }

        private static async Task<bool> ExecutePlaylistRoutineItemAsync(
            ScriptRuntimeService runtime,
            PlaylistItemDefinition item,
            IReadOnlyDictionary<string, ScriptRoutineDescriptor> routinesById)
        {
            if (string.IsNullOrWhiteSpace(item.RoutineId) || !routinesById.TryGetValue(item.RoutineId, out var routine))
            {
                Logger.WriteLine(Logger.LogLevel.Error, "Playlist item '{0}' routine not found ({1}).", item.DisplayName, item.RoutineId ?? "<null>");
                return false;
            }

            var arguments = new List<object?>();
            foreach (var parameter in routine.Parameters)
            {
                var displayName = parameter.DisplayName ?? parameter.Name;
                var raw = item.ParameterValues.TryGetValue(displayName, out var value)
                    ? value
                    : parameter.DefaultValue?.ToString() ?? string.Empty;

                if (!TryConvert(parameter.ParameterType, raw, out var converted))
                {
                    Logger.WriteLine(Logger.LogLevel.Error, "Invalid playlist parameter '{0}' value '{1}'", displayName, raw);
                    return false;
                }

                arguments.Add(converted);
            }

            var result = await runtime.ExecuteRoutineAsync(routine, arguments).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Logger.WriteLine(Logger.LogLevel.Error, "Routine failed: {0}", routine.Name);
                if (result.Exception != null)
                {
                    Logger.WriteLine(Logger.LogLevel.Error, "{0}", result.Exception);
                }
                return false;
            }

            Logger.WriteLine(Logger.LogLevel.Event, "Routine succeeded: {0}", routine.Name);
            return true;
        }

        private static async Task<bool> RunRoutineAsync(
            ScriptRuntimeService runtime,
            CliOptions options,
            IReadOnlyList<ScriptRoutineDescriptor> allRoutines)
        {
            if (string.IsNullOrWhiteSpace(options.RunRoutine))
            {
                Logger.WriteLine(Logger.LogLevel.Error, "Missing --run-routine value.");
                return false;
            }

            var routine = ResolveRoutine(allRoutines, options.RunRoutine);
            if (routine == null)
            {
                Logger.WriteLine(Logger.LogLevel.Error, "Routine '{0}' not found.", options.RunRoutine);
                return false;
            }

            var parameterValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in options.Parameters)
            {
                parameterValues[pair.Key] = pair.Value;
            }

            if (!string.IsNullOrWhiteSpace(options.ParametersFile))
            {
                foreach (var pair in LoadParameterFile(options.ParametersFile))
                {
                    parameterValues[pair.Key] = pair.Value;
                }
            }

            var arguments = new List<object?>();
            foreach (var parameter in routine.Parameters)
            {
                var displayName = parameter.DisplayName ?? parameter.Name;
                var raw = parameterValues.TryGetValue(displayName, out var value)
                    ? value
                    : parameterValues.TryGetValue(parameter.Name, out var byName)
                        ? byName
                        : parameter.DefaultValue?.ToString() ?? string.Empty;

                if (!TryConvert(parameter.ParameterType, raw, out var converted))
                {
                    Logger.WriteLine(Logger.LogLevel.Error, "Invalid value for parameter '{0}' ({1}).", displayName, parameter.ParameterType.Name);
                    return false;
                }

                arguments.Add(converted);
            }

            var result = await runtime.ExecuteRoutineAsync(routine, arguments).ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                Logger.WriteLine(Logger.LogLevel.Error, "Routine failed: {0}", routine.Name);
                if (result.Exception != null)
                {
                    Logger.WriteLine(Logger.LogLevel.Error, "{0}", result.Exception);
                }
                return false;
            }

            Logger.WriteLine(Logger.LogLevel.Event, "Routine succeeded: {0}", routine.Name);
            return true;
        }

        private static ScriptRoutineDescriptor? ResolveRoutine(IReadOnlyList<ScriptRoutineDescriptor> allRoutines, string runRoutine)
        {
            var byId = allRoutines.FirstOrDefault(r => string.Equals(GetRoutineKey(r), runRoutine, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
            {
                return byId;
            }

            var byName = allRoutines.Where(r => string.Equals(r.Name, runRoutine, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 1)
            {
                return byName[0];
            }

            return null;
        }

        private static Dictionary<string, string> LoadParameterFile(string filePath)
        {
            var fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(fullPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static List<PlaylistDefinition> LoadPlaylists(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<PlaylistDefinition>>(json) ?? new List<PlaylistDefinition>();
            }
            catch
            {
                return new List<PlaylistDefinition>();
            }
        }

        private static string GetRoutineKey(ScriptRoutineDescriptor routine)
        {
            return (routine.Method.DeclaringType?.FullName ?? "<unknown>") + "." + routine.Method.Name;
        }

        private static string ResolveScriptsRoot(CliOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.ScriptsRoot))
            {
                return Path.GetFullPath(options.ScriptsRoot);
            }

            var settings = new SettingsService(SettingsService.GetDefaultSettingsPath());
            if (!string.IsNullOrWhiteSpace(settings.ScriptsRoot))
            {
                return Path.GetFullPath(settings.ScriptsRoot);
            }

            return Path.Combine(AppContext.BaseDirectory, "Scripts");
        }

        private static CliOptions ParseArgs(string[] args)
        {
            var options = new CliOptions();

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                var next = i + 1 < args.Length ? args[i + 1] : null;

                if (arg.Equals("--scripts-root", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(next))
                {
                    options.ScriptsRoot = next;
                    i++;
                    continue;
                }

                if (arg.Equals("--run-playlist", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(next))
                {
                    options.RunPlaylist = next;
                    i++;
                    continue;
                }

                if (arg.Equals("--playlist-file", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(next))
                {
                    options.PlaylistFile = next;
                    i++;
                    continue;
                }

                if (arg.Equals("--run-routine", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(next))
                {
                    options.RunRoutine = next;
                    i++;
                    continue;
                }

                if (arg.Equals("--params-file", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(next))
                {
                    options.ParametersFile = next;
                    i++;
                    continue;
                }

                if (arg.Equals("--set", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(next))
                {
                    var separatorIndex = next.IndexOf('=');
                    if (separatorIndex > 0 && separatorIndex < next.Length - 1)
                    {
                        var key = next[..separatorIndex].Trim();
                        var value = next[(separatorIndex + 1)..].Trim();
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            options.Parameters[key] = value;
                        }
                    }

                    i++;
                    continue;
                }

                if (arg.Equals("--list-routines", StringComparison.OrdinalIgnoreCase))
                {
                    options.ListRoutines = true;
                    continue;
                }

                if (arg.Equals("--list-playlists", StringComparison.OrdinalIgnoreCase))
                {
                    options.ListPlaylists = true;
                    continue;
                }

                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
                {
                    options.ShowHelp = true;
                }
            }

            return options;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Scriptor CLI (.NET 10)");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project Scripts -- [options]");
            Console.WriteLine();
            Console.WriteLine("General:");
            Console.WriteLine("  --help | -h | /?                 Show this help output.");
            Console.WriteLine("  --scripts-root <path>            Override scripts root folder.");
            Console.WriteLine("  --gen-project | --generate-project");
            Console.WriteLine("                                   Generate debuggable script project/solution.");
            Console.WriteLine();
            Console.WriteLine("Discovery:");
            Console.WriteLine("  --list-routines                  List collections/routines and routine ids.");
            Console.WriteLine("  --list-playlists                 List playlists in playlists file.");
            Console.WriteLine("  --playlist-file <path>           Override playlists.json path.");
            Console.WriteLine();
            Console.WriteLine("Run routine:");
            Console.WriteLine("  --run-routine <name-or-id>       Run one routine by display name or routine id.");
            Console.WriteLine("  --params-file <path>             JSON object of parameter values.");
            Console.WriteLine("  --set <key=value>                Set/override single parameter (repeatable).");
            Console.WriteLine();
            Console.WriteLine("Run playlist:");
            Console.WriteLine("  --run-playlist <playlist-name>   Run one playlist by name.");
            Console.WriteLine("  --playlist-file <path>           Override playlists.json path.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  dotnet run --project Scripts -- --list-routines");
            Console.WriteLine("  dotnet run --project Scripts -- --run-routine \"Hello to a Person\" --set \"Person Name=CI Runner\"");
            Console.WriteLine("  dotnet run --project Scripts -- --run-routine Scripts.Scripting.HelloWorldScripts.HelloWorlder --params-file .\\ci\\params.json");
            Console.WriteLine("  dotnet run --project Scripts -- --run-playlist \"Nightly Build Playlist\"");
        }

        private static void Logger_Error(string format, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(format, args);
            Console.ResetColor();
        }

        private static void Logger_Warning(string format, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(format, args);
            Console.ResetColor();
        }

        private static void Logger_Event(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }

        private static bool TryReadSelection(int maxValue, out int index)
        {
            Console.Write("Enter Selection: ");
            if (int.TryParse(Console.ReadLine(), out var selection) && selection > 0 && selection <= maxValue)
            {
                index = selection - 1;
                return true;
            }

            index = -1;
            return false;
        }

        private static bool TryReadParameterValue(Type parameterType, string prompt, string? defaultValue, out object? value)
        {
            Console.Write(prompt);
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                input = defaultValue;
            }

            return TryConvert(parameterType, input, out value);
        }

        private static bool TryConvert(Type type, string? value, out object? output)
        {
            var targetType = Nullable.GetUnderlyingType(type) ?? type;
            var raw = value ?? string.Empty;

            if (targetType == typeof(string))
            {
                output = raw;
                return true;
            }

            if (targetType == typeof(int) && int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var i))
            {
                output = i;
                return true;
            }

            if (targetType == typeof(bool) && bool.TryParse(raw, out var b))
            {
                output = b;
                return true;
            }

            if (targetType == typeof(double) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                output = d;
                return true;
            }

            if (targetType == typeof(float) && float.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var f))
            {
                output = f;
                return true;
            }

            if (targetType == typeof(decimal) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
            {
                output = dec;
                return true;
            }

            if (targetType == typeof(long) && long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
            {
                output = l;
                return true;
            }

            if (targetType == typeof(FileInfo))
            {
                output = string.IsNullOrWhiteSpace(raw) ? null : new FileInfo(raw);
                return true;
            }

            if (targetType == typeof(DirectoryInfo))
            {
                output = string.IsNullOrWhiteSpace(raw) ? null : new DirectoryInfo(raw);
                return true;
            }

            if (targetType.IsEnum && Enum.TryParse(targetType, raw, true, out var e))
            {
                output = e;
                return true;
            }

            output = null;
            return false;
        }

        private sealed class CliOptions
        {
            public string? ScriptsRoot { get; set; }
            public string? RunPlaylist { get; set; }
            public string? PlaylistFile { get; set; }
            public string? RunRoutine { get; set; }
            public string? ParametersFile { get; set; }
            public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);
            public bool ListRoutines { get; set; }
            public bool ListPlaylists { get; set; }
            public bool ShowHelp { get; set; }
        }

        private enum PlaylistItemType
        {
            Routine,
            ParallelGroup,
        }

        private sealed class PlaylistItemDefinition
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string DisplayName { get; set; } = string.Empty;
            public PlaylistItemType Type { get; set; } = PlaylistItemType.Routine;
            public string? RoutineId { get; set; }
            public Dictionary<string, string> ParameterValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<PlaylistItemDefinition> Children { get; set; } = new();
        }

        private sealed class PlaylistDefinition
        {
            public string Name { get; set; } = string.Empty;
            public DateTimeOffset? LastEditedAt { get; set; }
            public List<PlaylistItemDefinition> Items { get; set; } = new();
        }
    }
}

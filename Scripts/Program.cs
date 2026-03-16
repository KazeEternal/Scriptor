using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Scripts.Scriptor;
using Scripts.Scriptor.Conductor;

namespace Scripts
{
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            Logger.Event += Logger_Event;
            Logger.Warning += Logger_Warning;
            Logger.Error += Logger_Error;

            var scriptsRoot = Path.Combine(AppContext.BaseDirectory, "Scripts");
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

                return;
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

            if (parameterType == typeof(string))
            {
                value = input ?? string.Empty;
                return true;
            }

            if (parameterType == typeof(int) && int.TryParse(input, out var intValue))
            {
                value = intValue;
                return true;
            }

            if (parameterType == typeof(bool) && bool.TryParse(input, out var boolValue))
            {
                value = boolValue;
                return true;
            }

            if (parameterType.IsEnum && Enum.TryParse(parameterType, input, true, out var enumValue))
            {
                value = enumValue;
                return true;
            }

            value = null;
            return false;
        }
    }
}

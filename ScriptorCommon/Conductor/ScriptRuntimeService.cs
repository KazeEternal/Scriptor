using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.XPath;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Scripts.Scriptor;
using Scripts.Scriptor.Attributor;

namespace Scripts.Scriptor.Conductor
{
    public sealed class ScriptRuntimeService : IDisposable
    {
        private static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(500);

        private readonly object _gate = new();
        private readonly Timer _debounceTimer;
        private readonly List<MetadataReference> _metadataReferences = new();
        private readonly List<string> _packageAssemblyPaths = new();
        private FileSystemWatcher? _watcher;
        private ScriptAssemblyLoadContext? _loadContext;
        private Assembly? _scriptAssembly;
        private string? _lastAssemblyPath;
        private bool _disposed;
        private string? _lastPackageDependencySignature;

        public ScriptRuntimeService(string scriptsRoot)
        {
            if (string.IsNullOrWhiteSpace(scriptsRoot))
            {
                throw new ArgumentException("Scripts root cannot be null or whitespace.", nameof(scriptsRoot));
            }

            ScriptsRoot = scriptsRoot;
            ScriptsAssemblyPath = Path.Combine(ScriptsRoot, ".scriptor", "CompiledScripts");
            _debounceTimer = new Timer(_ => ReloadScripts(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            BuildMetadataReferences();
        }

        public string ScriptsRoot { get; }

        public string ScriptsAssemblyPath { get; }

        public ScriptRuntimeSnapshot? CurrentSnapshot { get; private set; }

        public event EventHandler<ScriptRuntimeSnapshot>? ScriptsReloaded;

        public event EventHandler<IReadOnlyList<ScriptCompilationDiagnostic>>? CompilationFailed;

        public void StartWatching()
        {
            ThrowIfDisposed();

            Directory.CreateDirectory(ScriptsRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(ScriptsAssemblyPath)!);

            if (_watcher != null)
            {
                return;
            }

            _watcher = new FileSystemWatcher(ScriptsRoot, "*.cs")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = false
            };

            _watcher.Changed += OnScriptsChanged;
            _watcher.Created += OnScriptsChanged;
            _watcher.Deleted += OnScriptsChanged;
            _watcher.Renamed += OnScriptsChanged;

            _watcher.EnableRaisingEvents = true;

            ScheduleReload();
        }

        public void StopWatching()
        {
            ThrowIfDisposed();

            if (_watcher == null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnScriptsChanged;
            _watcher.Created -= OnScriptsChanged;
            _watcher.Deleted -= OnScriptsChanged;
            _watcher.Renamed -= OnScriptsChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        public ScriptCompilationResult ReloadScripts()
        {
            ThrowIfDisposed();

            ScriptCompilationResult compilationResult;
            string? oldAssemblyPath = null;
            lock (_gate)
            {
                BuildMetadataReferences();
                compilationResult = CompileScripts();
                if (!compilationResult.Succeeded)
                {
                    CompilationFailed?.Invoke(this, compilationResult.Diagnostics);
                    return compilationResult;
                }

                if (compilationResult.Diagnostics.Any(d => string.Equals(d.Severity, DiagnosticSeverity.Error.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    CompilationFailed?.Invoke(this, compilationResult.Diagnostics);
                }

                var newContext = new ScriptAssemblyLoadContext(compilationResult.AssemblyPath, _packageAssemblyPaths);
                var assembly = LoadAssemblyIntoContext(newContext, compilationResult.AssemblyPath);
                var snapshot = BuildSnapshot(assembly);

                var oldContext = _loadContext;
                oldAssemblyPath = _lastAssemblyPath;
                _loadContext = newContext;
                _scriptAssembly = assembly;
                _lastAssemblyPath = compilationResult.AssemblyPath;
                CurrentSnapshot = snapshot;

                ScriptsReloaded?.Invoke(this, snapshot);

                if (oldContext != null)
                {
                    oldContext.Unload();
                }
            }

            TriggerUnloadCleanup();
            DeleteOldAssembly(oldAssemblyPath);
            DeleteOldAssembly(compilationResult.AssemblyPath);
            return compilationResult;
        }

        public async Task<ScriptExecutionResult> ExecuteRoutineAsync(
            ScriptRoutineDescriptor routine,
            IReadOnlyList<object?> arguments,
            string? executionScopeId = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (routine == null)
            {
                throw new ArgumentNullException(nameof(routine));
            }

            return await Task.Run(() => ExecuteRoutineCore(routine, arguments, executionScopeId), cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            StopWatching();
            _debounceTimer.Dispose();
            _loadContext?.Unload();
            _disposed = true;
            TriggerUnloadCleanup();
        }

        private void OnScriptsChanged(object sender, FileSystemEventArgs e)
        {
            ScheduleReload();
        }

        private void ScheduleReload()
        {
            _debounceTimer.Change(ReloadDebounce, Timeout.InfiniteTimeSpan);
        }

        private ScriptExecutionResult ExecuteRoutineCore(
            ScriptRoutineDescriptor routine,
            IReadOnlyList<object?> arguments,
            string? executionScopeId)
        {
            var stopwatch = Stopwatch.StartNew();
            var start = DateTimeOffset.Now;
            var scopeId = string.IsNullOrWhiteSpace(executionScopeId) ? Guid.NewGuid().ToString("N") : executionScopeId;
            var context = new IScriptContext
            {
                Name = routine.Name,
                IsSuccess = true
            };

            try
            {
                using var scope = Logger.BeginScope(scopeId);
                var method = routine.Method;
                var methodParameters = method.GetParameters();
                var invokeArgs = new List<object?>(methodParameters.Length);
                var userArgIndex = 0;

                foreach (var parameter in methodParameters)
                {
                    if (parameter.ParameterType == typeof(IScriptContext))
                    {
                        invokeArgs.Add(context);
                        continue;
                    }

                    if (userArgIndex >= arguments.Count)
                    {
                        throw new ArgumentException("Not enough arguments provided for the routine.", nameof(arguments));
                    }

                    invokeArgs.Add(arguments[userArgIndex]);
                    userArgIndex++;
                }

                object? instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType!);
                method.Invoke(instance, invokeArgs.ToArray());

                stopwatch.Stop();
                return new ScriptExecutionResult(true, scopeId, start, context, null, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                context.IsSuccess = false;
                return new ScriptExecutionResult(false, scopeId, start, context, ex, stopwatch.Elapsed);
            }
        }

        private ScriptCompilationResult CompileScripts()
        {
            Directory.CreateDirectory(ScriptsAssemblyPath);
            var scriptFiles = Directory.EnumerateFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories).ToList();

            var fullAttempt = CompileFiles(scriptFiles);
            if (fullAttempt.Succeeded)
            {
                return fullAttempt;
            }

            var failedFiles = fullAttempt.Diagnostics
                .Where(d => string.Equals(d.Severity, DiagnosticSeverity.Error.ToString(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(d.FilePath))
                .Select(d => d.FilePath!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var survivingFiles = scriptFiles
                .Where(file => !failedFiles.Contains(file))
                .ToList();

            if (survivingFiles.Count == 0)
            {
                return fullAttempt;
            }

            var partialAttempt = CompileFiles(survivingFiles);
            if (!partialAttempt.Succeeded)
            {
                return fullAttempt;
            }

            var excludedDiagnostics = failedFiles.Select(file => new ScriptCompilationDiagnostic(
                "SCRIPT_EXCLUDED",
                $"Excluded script file due to compile errors: {Path.GetFileName(file)}",
                DiagnosticSeverity.Error.ToString(),
                file,
                null,
                null)).ToList();

            var fullAttemptErrorsForExcludedFiles = fullAttempt.Diagnostics
                .Where(d =>
                    string.Equals(d.Severity, DiagnosticSeverity.Error.ToString(), StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(d.FilePath) &&
                    failedFiles.Contains(d.FilePath!))
                .ToList();

            var combinedDiagnostics = partialAttempt.Diagnostics
                .Concat(fullAttemptErrorsForExcludedFiles)
                .Concat(excludedDiagnostics)
                .ToList();
            return partialAttempt with { Diagnostics = combinedDiagnostics };
        }

        private ScriptCompilationResult CompileFiles(IReadOnlyList<string> scriptFiles)
        {
            if (scriptFiles.Count == 0)
            {
                return new ScriptCompilationResult(false, new[]
                {
                    new ScriptCompilationDiagnostic("SCRIPT0001", "No script files were found to compile.", DiagnosticSeverity.Error.ToString(), null, null, null)
                }, string.Empty);
            }

            var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
            var syntaxTrees = scriptFiles
                .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
                .ToList();

            var compilation = CSharpCompilation.Create(
                "Scriptor.DynamicScripts",
                syntaxTrees,
                _metadataReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

            var outputPath = Path.Combine(ScriptsAssemblyPath, $"CompiledScripts.{Guid.NewGuid():N}.dll");
            using var assemblyStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            var emitResult = compilation.Emit(assemblyStream);

            var diagnostics = emitResult.Diagnostics
                .Where(diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
                .Select(diagnostic =>
                {
                    var location = diagnostic.Location.GetMappedLineSpan();
                    return new ScriptCompilationDiagnostic(
                        diagnostic.Id,
                        diagnostic.GetMessage(),
                        diagnostic.Severity.ToString(),
                        location.Path,
                        location.StartLinePosition.Line + 1,
                        location.StartLinePosition.Character + 1);
                })
                .ToList();

            return new ScriptCompilationResult(emitResult.Success, diagnostics, outputPath);
        }

        private ScriptRuntimeSnapshot BuildSnapshot(Assembly assembly)
        {
            var collectionType = typeof(IScriptCollection);
            var collections = new List<ScriptCollectionDescriptor>();
            var dependencies = new List<ScriptPackageDependency>();

            AddDependenciesFromAttributes(assembly.GetCustomAttributes<ScriptPackageDependencyAttribute>(), dependencies);

            foreach (var type in assembly.GetTypes().Where(t => collectionType.IsAssignableFrom(t) && t != collectionType))
            {
                AddDependenciesFromAttributes(type.GetCustomAttributes<ScriptPackageDependencyAttribute>(), dependencies);
                var collectionName = type.GetCustomAttribute<ScriptCollectionNameAttribute>()?.Name ?? type.Name;
                var collectionDescription = type.GetCustomAttribute<ScriptCollectionDescriptionAttribute>()?.Description;

                var routines = new List<ScriptRoutineDescriptor>();
                var methods = type.GetMethods()
                    .Where(method => method.GetCustomAttributes(typeof(ScriptRoutineAttribute), true).Length > 0)
                    .ToList();

                foreach (var method in methods)
                {
                    AddDependenciesFromAttributes(method.GetCustomAttributes<ScriptPackageDependencyAttribute>(), dependencies);
                    var routineAttribute = method.GetCustomAttributes(typeof(ScriptRoutineAttribute), true)
                        .OfType<ScriptRoutineAttribute>()
                        .FirstOrDefault();

                    var routineName = routineAttribute?.Name ?? method.Name;
                    var routineDescription = routineAttribute?.Description;

                    var parameters = new List<ScriptParameterDescriptor>();
                    foreach (var parameter in method.GetParameters())
                    {
                        if (parameter.ParameterType == typeof(IScriptContext))
                        {
                            continue;
                        }

                        var paramAttribute = parameter.GetCustomAttribute<ParameterAttribute>();
                        parameters.Add(new ScriptParameterDescriptor(
                            parameter.Name ?? string.Empty,
                            parameter.ParameterType,
                            paramAttribute?.Name,
                            paramAttribute?.Description,
                            paramAttribute?.Usage,
                            paramAttribute?.Default));
                    }

                    routines.Add(new ScriptRoutineDescriptor(routineName, routineDescription, method, parameters));
                }

                collections.Add(new ScriptCollectionDescriptor(collectionName, collectionDescription, type, routines));
            }

            var distinctDependencies = dependencies
                .DistinctBy(dependency => (dependency.PackageId, dependency.Version))
                .ToList();

            return new ScriptRuntimeSnapshot(collections, distinctDependencies);
        }

        private static void AddDependenciesFromAttributes(
            IEnumerable<ScriptPackageDependencyAttribute> attributes,
            ICollection<ScriptPackageDependency> target)
        {
            foreach (var attribute in attributes)
            {
                if (string.IsNullOrWhiteSpace(attribute.PackageId))
                {
                    continue;
                }

                target.Add(new ScriptPackageDependency(attribute.PackageId, attribute.Version));
            }
        }

        private void BuildMetadataReferences()
        {
            _metadataReferences.Clear();
            _packageAssemblyPaths.Clear();
            var assemblyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddAssembly(Assembly assembly)
            {
                if (assembly.IsDynamic || string.IsNullOrWhiteSpace(assembly.Location))
                {
                    return;
                }

                if (assemblyPaths.Add(assembly.Location))
                {
                    _metadataReferences.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                AddAssembly(assembly);
            }

            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            {
                foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    try
                    {
                        if (!assemblyPaths.Add(path))
                        {
                            continue;
                        }

                        _metadataReferences.Add(MetadataReference.CreateFromFile(path));
                    }
                    catch
                    {
                    }
                }
            }

            AddAssembly(typeof(HttpClient).Assembly);
            AddAssembly(typeof(WebClient).Assembly);
            AddAssembly(typeof(ZipFile).Assembly);
            AddAssembly(typeof(Regex).Assembly);
            AddAssembly(typeof(Parallel).Assembly);
            AddAssembly(typeof(Enumerable).Assembly);
            AddAssembly(typeof(XmlReader).Assembly);
            AddAssembly(typeof(XPathExpression).Assembly);

            var packageDependencies = DiscoverPackageDependenciesFromScripts();
            if (packageDependencies.Count > 0)
            {
                Logger.WriteLine(
                    Logger.LogLevel.Event,
                    "Detected script package dependencies: {0}",
                    string.Join(", ", packageDependencies.Select(d => string.IsNullOrWhiteSpace(d.Version) ? d.PackageId : $"{d.PackageId} ({d.Version})")));
            }
            AddPackageAssemblyReferences(packageDependencies, assemblyPaths);

            var htmlAgilityPack = TryLoadAssembly("HtmlAgilityPack");
            if (htmlAgilityPack != null)
            {
                AddAssembly(htmlAgilityPack);
            }
        }

        private IReadOnlyList<ScriptPackageDependency> DiscoverPackageDependenciesFromScripts()
        {
            if (!Directory.Exists(ScriptsRoot))
            {
                return Array.Empty<ScriptPackageDependency>();
            }

            var regex = new Regex("ScriptPackageDependency\\s*\\(\\s*\"(?<id>[^\"]+)\"(?:\\s*,\\s*\"(?<version>[^\"]+)\")?", RegexOptions.Compiled);
            var results = new Dictionary<string, ScriptPackageDependency>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.EnumerateFiles(ScriptsRoot, "*.cs", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(file);
                var matches = regex.Matches(content);
                foreach (Match match in matches)
                {
                    if (!match.Success)
                    {
                        continue;
                    }

                    var id = match.Groups["id"].Value.Trim();
                    var version = match.Groups["version"].Success ? match.Groups["version"].Value.Trim() : null;
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    results[id] = new ScriptPackageDependency(id, string.IsNullOrWhiteSpace(version) ? null : version);
                }
            }

            return results.Values.ToList();
        }

        private void AddPackageAssemblyReferences(
            IReadOnlyList<ScriptPackageDependency> dependencies,
            ISet<string> assemblyPaths)
        {
            if (dependencies.Count == 0)
            {
                return;
            }

            var signature = string.Join("|", dependencies
                .OrderBy(d => d.PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Version, StringComparer.OrdinalIgnoreCase)
                .Select(d => $"{d.PackageId}:{d.Version}"));

            if (!string.Equals(_lastPackageDependencySignature, signature, StringComparison.OrdinalIgnoreCase))
            {
                var restored = RestoreScriptPackageProject(dependencies);
                if (!restored)
                {
                    Logger.WriteLine(Logger.LogLevel.Warning, "Package restore did not complete successfully. Script package references may be unavailable.");
                }
                _lastPackageDependencySignature = signature;
            }

            var globalPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
            if (string.IsNullOrWhiteSpace(globalPackages))
            {
                globalPackages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            }

            if (!Directory.Exists(globalPackages))
            {
                Logger.WriteLine(Logger.LogLevel.Warning, "NuGet package cache directory not found: {0}", globalPackages);
                return;
            }

            foreach (var dependency in dependencies)
            {
                var packageRoot = Path.Combine(globalPackages, dependency.PackageId.ToLowerInvariant());
                if (!Directory.Exists(packageRoot))
                {
                    Logger.WriteLine(Logger.LogLevel.Warning, "Package not found in cache after restore: {0}", dependency.PackageId);
                    continue;
                }

                var versionFolder = ResolvePackageVersionFolder(packageRoot, dependency.Version);
                if (versionFolder == null)
                {
                    Logger.WriteLine(Logger.LogLevel.Warning, "Unable to resolve package version folder for: {0}", dependency.PackageId);
                    continue;
                }

                var libFolder = ResolveBestLibFolder(versionFolder);
                if (libFolder == null)
                {
                    Logger.WriteLine(Logger.LogLevel.Warning, "No compatible lib folder found for package: {0}", dependency.PackageId);
                    continue;
                }

                Logger.WriteLine(Logger.LogLevel.Event, "Resolved package '{0}' to '{1}'", dependency.PackageId, libFolder);

                var addedReferenceCount = 0;
                var mappedRuntimeCount = 0;

                foreach (var dll in Directory.EnumerateFiles(libFolder, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if (!_packageAssemblyPaths.Contains(dll, StringComparer.OrdinalIgnoreCase))
                        {
                            _packageAssemblyPaths.Add(dll);
                            mappedRuntimeCount++;
                        }

                        if (assemblyPaths.Add(dll))
                        {
                            _metadataReferences.Add(MetadataReference.CreateFromFile(dll));
                            addedReferenceCount++;
                        }
                    }
                    catch
                    {
                    }
                }

                Logger.WriteLine(
                    Logger.LogLevel.Event,
                    "Added {0} package compile reference(s), mapped {1} runtime assembly path(s) for {2}.",
                    addedReferenceCount,
                    mappedRuntimeCount,
                    dependency.PackageId);
            }
        }

        private bool RestoreScriptPackageProject(IReadOnlyList<ScriptPackageDependency> dependencies)
        {
            try
            {
                Logger.WriteLine(Logger.LogLevel.Event, "Restoring script package dependencies ({0})...", dependencies.Count);
                var generated = ScriptProjectGenerator.EnsureScriptProject(
                    ScriptsRoot,
                    dependencies,
                    commonProjectPath: null,
                    commonAssemblyPath: typeof(Logger).Assembly.Location);

                Logger.WriteLine(Logger.LogLevel.Event, "Generated/updated script package project: {0}", generated.ProjectPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"restore \"{generated.ProjectPath}\"",
                    WorkingDirectory = Path.GetDirectoryName(generated.ProjectPath) ?? ScriptsRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Logger.WriteLine(Logger.LogLevel.Error, "Failed to start 'dotnet restore' process.");
                    return false;
                }

                process.WaitForExit();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    Logger.WriteLine(Logger.LogLevel.Event, "dotnet restore output: {0}", output.Trim());
                }

                if (process.ExitCode != 0)
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        Logger.WriteLine(Logger.LogLevel.Error, "dotnet restore failed: {0}", error.Trim());
                    }

                    Logger.WriteLine(Logger.LogLevel.Error, "dotnet restore exit code: {0}", process.ExitCode);

                    return false;
                }

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Logger.WriteLine(Logger.LogLevel.Warning, "dotnet restore warnings: {0}", error.Trim());
                }

                Logger.WriteLine(Logger.LogLevel.Event, "Package restore completed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteLine(Logger.LogLevel.Error, "Package restore failed with exception: {0}", ex.Message);
                return false;
            }
        }

        private static string? ResolvePackageVersionFolder(string packageRoot, string? requestedVersion)
        {
            if (!string.IsNullOrWhiteSpace(requestedVersion))
            {
                var exact = Path.Combine(packageRoot, requestedVersion);
                if (Directory.Exists(exact))
                {
                    return exact;
                }
            }

            return Directory.EnumerateDirectories(packageRoot)
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static string? ResolveBestLibFolder(string versionFolder)
        {
            var libRoot = Path.Combine(versionFolder, "lib");
            if (!Directory.Exists(libRoot))
            {
                return null;
            }

            var priorities = new[]
            {
                "net10.0",
                "net9.0",
                "net8.0",
                "net7.0",
                "net6.0",
                "netstandard2.1",
                "netstandard2.0"
            };

            foreach (var tfm in priorities)
            {
                var candidate = Path.Combine(libRoot, tfm);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Directory.EnumerateDirectories(libRoot)
                .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static Assembly? TryLoadAssembly(string name)
        {
            try
            {
                return Assembly.Load(new AssemblyName(name));
            }
            catch
            {
                return null;
            }
        }

        private void TriggerUnloadCleanup()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void DeleteOldAssembly(string? assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                return;
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(assemblyPath))
                    {
                        File.Delete(assemblyPath);
                    }

                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(100);
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private static Assembly LoadAssemblyIntoContext(AssemblyLoadContext context, string assemblyPath)
        {
            using var fileStream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var memoryStream = new MemoryStream();
            fileStream.CopyTo(memoryStream);
            memoryStream.Position = 0;
            return context.LoadFromStream(memoryStream);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ScriptRuntimeService));
            }
        }

        private sealed class ScriptAssemblyLoadContext : AssemblyLoadContext
        {
            private readonly AssemblyDependencyResolver _resolver;
            private readonly Dictionary<string, string> _packageAssemblyMap;
            private readonly HashSet<string> _unresolvedAssemblyNames = new(StringComparer.OrdinalIgnoreCase);

            public ScriptAssemblyLoadContext(string mainAssemblyPath, IEnumerable<string> packageAssemblyPaths)
                : base(isCollectible: true)
            {
                _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
                _packageAssemblyMap = packageAssemblyPaths
                    .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
                if (resolvedPath != null)
                {
                    return LoadFromAssemblyPath(resolvedPath);
                }

                if (_packageAssemblyMap.TryGetValue(assemblyName.Name ?? string.Empty, out var packagePath))
                {
                    Logger.WriteLine(Logger.LogLevel.Event, "Loading package assembly '{0}' from '{1}'", assemblyName.Name ?? "<unknown>", packagePath);
                    return LoadFromAssemblyPath(packagePath);
                }

                var name = assemblyName.Name ?? string.Empty;
                if (name.Length > 0 &&
                    !name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "System", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "netstandard", StringComparison.OrdinalIgnoreCase) &&
                    _unresolvedAssemblyNames.Add(name))
                {
                    Logger.WriteLine(Logger.LogLevel.Warning, "Assembly load unresolved in script context: {0}", name);
                }

                return null;
            }
        }
    }
}

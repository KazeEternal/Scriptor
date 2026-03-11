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
using Scripts.Scriptor.Attributor;

namespace Scripts.Scriptor.Conductor
{
    public sealed class ScriptRuntimeService : IDisposable
    {
        private static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(500);

        private readonly object _gate = new();
        private readonly Timer _debounceTimer;
        private readonly List<MetadataReference> _metadataReferences = new();
        private FileSystemWatcher? _watcher;
        private ScriptAssemblyLoadContext? _loadContext;
        private Assembly? _scriptAssembly;
        private string? _lastAssemblyPath;
        private bool _disposed;

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

                var newContext = new ScriptAssemblyLoadContext(compilationResult.AssemblyPath);
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
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            if (routine == null)
            {
                throw new ArgumentNullException(nameof(routine));
            }

            return await Task.Run(() => ExecuteRoutineCore(routine, arguments), cancellationToken).ConfigureAwait(false);
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

        private ScriptExecutionResult ExecuteRoutineCore(ScriptRoutineDescriptor routine, IReadOnlyList<object?> arguments)
        {
            var stopwatch = Stopwatch.StartNew();
            var context = new IScriptContext
            {
                Name = routine.Name,
                IsSuccess = true
            };

            try
            {
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
                return new ScriptExecutionResult(true, context, null, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                context.IsSuccess = false;
                return new ScriptExecutionResult(false, context, ex, stopwatch.Elapsed);
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

            var combinedDiagnostics = partialAttempt.Diagnostics.Concat(excludedDiagnostics).ToList();
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

            AddAssembly(typeof(HttpClient).Assembly);
            AddAssembly(typeof(WebClient).Assembly);
            AddAssembly(typeof(ZipFile).Assembly);
            AddAssembly(typeof(Regex).Assembly);
            AddAssembly(typeof(XmlReader).Assembly);
            AddAssembly(typeof(XPathExpression).Assembly);

            var htmlAgilityPack = TryLoadAssembly("HtmlAgilityPack");
            if (htmlAgilityPack != null)
            {
                AddAssembly(htmlAgilityPack);
            }
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

            public ScriptAssemblyLoadContext(string mainAssemblyPath)
                : base(isCollectible: true)
            {
                _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
            }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
                if (resolvedPath != null)
                {
                    return LoadFromAssemblyPath(resolvedPath);
                }

                return null;
            }
        }
    }
}

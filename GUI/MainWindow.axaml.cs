using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using GUI.ViewModel;
using Scripts.Scriptor;
using Scripts.Scriptor.Conductor;

namespace GUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly ScriptRuntimeService _runtime;
        private readonly List<ParameterViewModel> _parameterViewModels = new();
        private readonly ObservableCollection<ScriptNode> _treeNodes = new();
        private readonly Dictionary<string, ScriptRoutineDescriptor> _routinesById = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<PlaylistDefinition> _playlists = new();
        private readonly Dictionary<string, RoutineRunRowUi> _runRowsByScope = new(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];
        private readonly DispatcherTimer _statusSpinnerTimer = new() { Interval = TimeSpan.FromMilliseconds(110) };
        private int _spinnerFrameIndex;
        private static readonly IBrush PanelBorderBrush = new SolidColorBrush(Color.Parse("#3F3F46"));
        private static readonly IBrush PanelFillBrush = new SolidColorBrush(Color.Parse("#1E1E1E"));
        private static readonly IBrush RunningStatusBrush = new SolidColorBrush(Color.Parse("#00BCF2"));
        private static readonly IBrush SuccessStatusBrush = new SolidColorBrush(Color.Parse("#16C60C"));
        private static readonly IBrush FailureStatusBrush = new SolidColorBrush(Color.Parse("#F44747"));
        private static readonly IBrush IdleStatusBrush = new SolidColorBrush(Color.Parse("#808080"));
        private static readonly IBrush StatusTextOnBadgeBrush = new SolidColorBrush(Color.Parse("#FFFFFF"));

        private TreeView _collectionsTree = null!;
        private TextBlock _collectionDescriptionBox = null!;
        private StackPanel _parameterPanel = null!;
        private TextBlock _routineDescriptionBox = null!;
        private Button _reloadButton = null!;
        private Button _runButton = null!;
        private Button _newPlaylistButton = null!;
        private Button _addToPlaylistButton = null!;
        private Button _generateProjectButton = null!;
        private Button _runSelectedButton = null!;
        private TextBlock _scriptPathStatus = null!;
        private StackPanel _runLogRowsPanel = null!;
        private ScrollViewer _runLogScrollViewer = null!;

        private ScriptRoutineDescriptor? _currentRoutine;
        private ScriptNode? _selectedNode;
        private Action<List<ParameterViewModel>>? _saveDefaultsAction;

        public MainWindow()
        {
            InitializeComponent();

            _reloadButton.Click += ReloadButton_Click;
            _runButton.Click += RunButton_Click;
            _newPlaylistButton.Click += NewPlaylistButton_Click;
            _addToPlaylistButton.Click += AddToPlaylistButton_Click;
            _generateProjectButton.Click += GenerateProjectButton_Click;
            _runSelectedButton.Click += RunButton_Click;
            _collectionsTree.SelectionChanged += CollectionsTree_SelectionChanged;

            var scriptsRoot = ResolveScriptsRoot();
            Logger.ConfigureFileLogging(scriptsRoot);
            Title = $"Scriptor GUI - {scriptsRoot}";
            _scriptPathStatus.Text = $"Scripts path: {scriptsRoot}";
            _runtime = new ScriptRuntimeService(scriptsRoot);
            _runtime.ScriptsReloaded += Runtime_ScriptsReloaded;
            _runtime.CompilationFailed += Runtime_CompilationFailed;

            Logger.EntryWritten += Logger_EntryWritten;

            _statusSpinnerTimer.Tick += StatusSpinnerTimer_Tick;
            _statusSpinnerTimer.Start();

            _runtime.StartWatching();
            _runtime.ReloadScripts();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _collectionsTree = this.FindControl<TreeView>("CollectionsTree") ?? throw new InvalidOperationException("CollectionsTree not found.");
            _collectionDescriptionBox = this.FindControl<TextBlock>("CollectionDescription") ?? throw new InvalidOperationException("CollectionDescription not found.");
            _parameterPanel = this.FindControl<StackPanel>("ParameterPanel") ?? throw new InvalidOperationException("ParameterPanel not found.");
            _routineDescriptionBox = this.FindControl<TextBlock>("RoutineDescription") ?? throw new InvalidOperationException("RoutineDescription not found.");
            _reloadButton = this.FindControl<Button>("ReloadButton") ?? throw new InvalidOperationException("ReloadButton not found.");
            _runButton = this.FindControl<Button>("RunButton") ?? throw new InvalidOperationException("RunButton not found.");
            _newPlaylistButton = this.FindControl<Button>("NewPlaylistButton") ?? throw new InvalidOperationException("NewPlaylistButton not found.");
            _addToPlaylistButton = this.FindControl<Button>("AddToPlaylistButton") ?? throw new InvalidOperationException("AddToPlaylistButton not found.");
            _generateProjectButton = this.FindControl<Button>("GenerateProjectButton") ?? throw new InvalidOperationException("GenerateProjectButton not found.");
            _runSelectedButton = this.FindControl<Button>("RunSelectedButton") ?? throw new InvalidOperationException("RunSelectedButton not found.");
            _scriptPathStatus = this.FindControl<TextBlock>("ScriptPathStatus") ?? throw new InvalidOperationException("ScriptPathStatus not found.");
            _runLogRowsPanel = this.FindControl<StackPanel>("RunLogRowsPanel") ?? throw new InvalidOperationException("RunLogRowsPanel not found.");
            _runLogScrollViewer = this.FindControl<ScrollViewer>("RunLogScrollViewer") ?? throw new InvalidOperationException("RunLogScrollViewer not found.");
        }

        private void Runtime_ScriptsReloaded(object? sender, ScriptRuntimeSnapshot snapshot)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _routinesById.Clear();
                foreach (var collection in snapshot.Collections)
                {
                    foreach (var routine in collection.Routines)
                    {
                        _routinesById[GetRoutineKey(routine)] = routine;
                    }
                }

                _playlists.Clear();
                _playlists.AddRange(LoadPlaylists());

                RebuildOperationsTree(snapshot);

                AppendLog($"Loaded {snapshot.Collections.Count} collections.");
            });
        }

        private void RebuildOperationsTree(
            ScriptRuntimeSnapshot snapshot,
            string? selectPlaylistName = null,
            string? selectPlaylistItemId = null)
        {
            _treeNodes.Clear();
            _collectionDescriptionBox.Text = string.Empty;

            BuildOperationsTree(snapshot);
            _collectionsTree.ItemsSource = _treeNodes;

            if (!string.IsNullOrWhiteSpace(selectPlaylistItemId))
            {
                var itemNode = FindNode(_treeNodes, node => node.PlaylistItem?.Id == selectPlaylistItemId);
                if (itemNode != null)
                {
                    _collectionsTree.SelectedItem = itemNode;
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(selectPlaylistName))
            {
                var playlistNode = FindNode(_treeNodes, node => node.Kind == ScriptNodeKind.Playlist && string.Equals(node.Name, selectPlaylistName, StringComparison.OrdinalIgnoreCase));
                if (playlistNode != null)
                {
                    _collectionsTree.SelectedItem = playlistNode;
                }
            }
        }

        private static ScriptNode? FindNode(IEnumerable<ScriptNode> nodes, Func<ScriptNode, bool> predicate)
        {
            foreach (var node in nodes)
            {
                if (predicate(node))
                {
                    return node;
                }

                var child = FindNode(node.Children, predicate);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private void BuildOperationsTree(ScriptRuntimeSnapshot snapshot)
        {
            var collectionsRoot = new ScriptNode { Name = "Collections", Kind = ScriptNodeKind.CollectionsRoot };
            var playlistsRoot = new ScriptNode { Name = "PlayLists", Kind = ScriptNodeKind.PlaylistsRoot };

            foreach (var collection in snapshot.Collections)
            {
                var collectionNode = new ScriptNode
                {
                    Name = $"{collection.Name} ({collection.Routines.Count} scripts)",
                    Description = collection.Description ?? string.Empty,
                    Collection = collection,
                    Kind = ScriptNodeKind.Collection,
                };

                foreach (var routine in collection.Routines)
                {
                    collectionNode.Children.Add(new ScriptNode
                    {
                        Name = routine.Name,
                        Description = routine.Description ?? string.Empty,
                        Collection = collection,
                        Routine = routine,
                        Kind = ScriptNodeKind.Routine,
                    });
                }

                collectionsRoot.Children.Add(collectionNode);
            }

            foreach (var playlist in _playlists)
            {
                playlistsRoot.Children.Add(BuildPlaylistNode(playlist));
            }

            _treeNodes.Add(collectionsRoot);
            _treeNodes.Add(playlistsRoot);
        }

        private ScriptNode BuildPlaylistNode(PlaylistDefinition playlist)
        {
            var playlistNode = new ScriptNode
            {
                Name = playlist.Name,
                Kind = ScriptNodeKind.Playlist,
                Playlist = playlist,
                Description = "Playlist execution (sequential; parallel groups run concurrently).",
            };

            foreach (var item in playlist.Items)
            {
                playlistNode.Children.Add(BuildPlaylistItemNode(playlist, item));
            }

            return playlistNode;
        }

        private ScriptNode BuildPlaylistItemNode(PlaylistDefinition playlist, PlaylistItemDefinition item)
        {
            if (item.Type == PlaylistItemType.ParallelGroup)
            {
                var parallelNode = new ScriptNode
                {
                    Name = string.IsNullOrWhiteSpace(item.DisplayName) ? "Parallel Routines" : item.DisplayName,
                    Kind = ScriptNodeKind.PlaylistParallelGroup,
                    Playlist = playlist,
                    PlaylistItem = item,
                };

                foreach (var child in item.Children)
                {
                    parallelNode.Children.Add(BuildPlaylistItemNode(playlist, child));
                }

                return parallelNode;
            }

            ScriptRoutineDescriptor? routine = null;
            if (!string.IsNullOrWhiteSpace(item.RoutineId))
            {
                _routinesById.TryGetValue(item.RoutineId, out routine);
            }

            return new ScriptNode
            {
                Name = routine?.Name ?? item.DisplayName,
                Description = routine?.Description ?? string.Empty,
                Kind = ScriptNodeKind.PlaylistRoutine,
                Playlist = playlist,
                PlaylistItem = item,
                Routine = routine,
            };
        }

        private void CollectionsTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_collectionsTree.SelectedItem is not ScriptNode node)
            {
                return;
            }

            _selectedNode = node;

            if (!string.IsNullOrWhiteSpace(node.Description))
            {
                _collectionDescriptionBox.Text = node.Description;
            }

            if (node.Kind == ScriptNodeKind.Routine && node.Routine != null)
            {
                DisplayRoutine(node.Routine, null, SaveRoutineDefaults);
            }

            if (node.Kind == ScriptNodeKind.PlaylistRoutine && node.Routine != null && node.PlaylistItem != null)
            {
                DisplayRoutine(node.Routine, node.PlaylistItem.ParameterValues, values => SavePlaylistItemDefaults(node.PlaylistItem!, values));
            }
        }

        private void DisplayRoutine(
            ScriptRoutineDescriptor routine,
            Dictionary<string, string>? overrideDefaults,
            Action<List<ParameterViewModel>> saveAction)
        {
            SaveCurrentRoutineDefaults();

            _currentRoutine = routine;
            _saveDefaultsAction = saveAction;
            _routineDescriptionBox.Text = routine.Description ?? string.Empty;
            _parameterViewModels.Clear();
            _parameterPanel.Children.Clear();

            var defaults = LoadDefaultsFile();
            defaults.TryGetValue(GetRoutineKey(routine), out var saved);

            foreach (var parameter in routine.Parameters)
            {
                var name = parameter.DisplayName ?? parameter.Name;
                var value = parameter.DefaultValue?.ToString() ?? string.Empty;
                if (overrideDefaults != null && overrideDefaults.TryGetValue(name, out var overriddenValue))
                {
                    value = overriddenValue;
                }
                if (saved != null && saved.TryGetValue(name, out var savedValue))
                {
                    value = savedValue;
                }

                var vm = new ParameterViewModel
                {
                    Name = name,
                    Value = value,
                    Description = parameter.Description ?? string.Empty,
                    Usage = parameter.Usage ?? string.Empty
                };
                _parameterViewModels.Add(vm);

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*"), Margin = new Avalonia.Thickness(0, 0, 0, 6) };

                var nameBox = new TextBlock
                {
                    Text = vm.Name,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
                var tip = string.IsNullOrWhiteSpace(vm.Description) && string.IsNullOrWhiteSpace(vm.Usage)
                    ? null
                    : $"{vm.Description}\n{vm.Usage}";
                if (!string.IsNullOrWhiteSpace(tip))
                {
                    Avalonia.Controls.ToolTip.SetTip(nameBox, tip);
                }
                nameBox.DoubleTapped += (_, _) =>
                {
                    _routineDescriptionBox.Text = string.IsNullOrWhiteSpace(vm.Usage)
                        ? vm.Description
                        : $"{vm.Description}\nUsage: {vm.Usage}";
                };

                var input = new TextBox { Text = vm.Value };
                input.LostFocus += (_, _) => SaveParameter(vm, input.Text);
                input.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        SaveParameter(vm, input.Text);
                    }
                };

                Grid.SetColumn(nameBox, 0);
                Grid.SetColumn(input, 1);
                row.Children.Add(nameBox);
                row.Children.Add(input);
                _parameterPanel.Children.Add(row);
            }
        }

        private void SaveParameter(ParameterViewModel vm, string? newValue)
        {
            vm.Value = newValue ?? string.Empty;
            SaveCurrentRoutineDefaults();
        }

        private void SaveCurrentRoutineDefaults()
        {
            if (_currentRoutine == null || _saveDefaultsAction == null)
            {
                return;
            }

            _saveDefaultsAction(_parameterViewModels);
        }

        private void SaveRoutineDefaults(List<ParameterViewModel> values)
        {
            if (_currentRoutine == null)
            {
                return;
            }

            var defaults = LoadDefaultsFile();
            defaults[GetRoutineKey(_currentRoutine)] = values.ToDictionary(p => p.Name, p => p.Value);
            SaveDefaultsFile(defaults);
        }

        private void SavePlaylistItemDefaults(PlaylistItemDefinition item, List<ParameterViewModel> values)
        {
            item.ParameterValues = values.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase);
            SavePlaylists(_playlists);
        }

        private async void RunButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_selectedNode?.Kind == ScriptNodeKind.Playlist && _selectedNode.Playlist != null)
            {
                ClearRunLog();
                await ExecutePlaylistAsync(_selectedNode.Playlist).ConfigureAwait(false);
                return;
            }

            if (_currentRoutine == null)
            {
                AppendLog("Select a routine to run.");
                return;
            }

            var converted = new List<object?>();
            for (var i = 0; i < _currentRoutine.Parameters.Count; i++)
            {
                var parameter = _currentRoutine.Parameters[i];
                var vm = _parameterViewModels[i];
                if (!TryConvert(parameter.ParameterType, vm.Value, out var value))
                {
                    AppendLog($"Invalid value for {vm.Name} ({parameter.ParameterType.Name})");
                    return;
                }
                converted.Add(value);
            }

            SaveCurrentRoutineDefaults();
            ClearRunLog();
            var scopeId = Guid.NewGuid().ToString("N");
            var row = StartRunRow(scopeId, _currentRoutine.Name, DateTimeOffset.Now);
            AddRunMessage(scopeId, $"Running {_currentRoutine.Name}...");

            var result = await _runtime.ExecuteRoutineAsync(_currentRoutine, converted, scopeId).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                CompleteRunRow(result.ExecutionScopeId, result.IsSuccess, result.Duration, result.StartedAt);
                if (result.Exception != null)
                {
                    AddRunMessage(result.ExecutionScopeId, result.Exception.ToString(), Logger.LogLevel.Error);
                }
            });
        }

        private async Task ExecutePlaylistAsync(PlaylistDefinition playlist)
        {
            ClearRunLog();
            AppendLog($"Running playlist {playlist.Name}...");
            foreach (var item in playlist.Items)
            {
                if (item.Type == PlaylistItemType.ParallelGroup)
                {
                    var tasks = item.Children
                        .Where(child => child.Type == PlaylistItemType.Routine)
                        .Select(child => ExecutePlaylistRoutineItemAsync(child));
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                else
                {
                    await ExecutePlaylistRoutineItemAsync(item).ConfigureAwait(false);
                }
            }

            Dispatcher.UIThread.Post(() => AppendLog($"Playlist {playlist.Name} completed."));
        }

        private async Task ExecutePlaylistRoutineItemAsync(PlaylistItemDefinition item)
        {
            if (string.IsNullOrWhiteSpace(item.RoutineId) || !_routinesById.TryGetValue(item.RoutineId, out var routine))
            {
                Dispatcher.UIThread.Post(() => AppendLog($"Playlist item '{item.DisplayName}' routine not found."));
                return;
            }

            var scopeId = Guid.NewGuid().ToString("N");
            Dispatcher.UIThread.Post(() =>
            {
                StartRunRow(scopeId, item.DisplayName, DateTimeOffset.Now);
                AddRunMessage(scopeId, $"Running playlist item {item.DisplayName}...");
            });

            var args = new List<object?>();
            foreach (var parameter in routine.Parameters)
            {
                var name = parameter.DisplayName ?? parameter.Name;
                var raw = item.ParameterValues.TryGetValue(name, out var value)
                    ? value
                    : parameter.DefaultValue?.ToString() ?? string.Empty;

                if (!TryConvert(parameter.ParameterType, raw, out var converted))
                {
                    Dispatcher.UIThread.Post(() => AppendLog($"Invalid playlist parameter value for {name} in item {item.DisplayName}."));
                    return;
                }

                args.Add(converted);
            }

            var result = await _runtime.ExecuteRoutineAsync(routine, args, scopeId).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                CompleteRunRow(result.ExecutionScopeId, result.IsSuccess, result.Duration, result.StartedAt);

                if (result.Exception != null)
                {
                    AddRunMessage(result.ExecutionScopeId, result.Exception.ToString(), Logger.LogLevel.Error);
                }
            });
        }

        private void NewPlaylistButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var baseName = "New Playlist";
            var name = baseName;
            var index = 1;
            while (_playlists.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                index++;
                name = $"{baseName} {index}";
            }

            _playlists.Add(new PlaylistDefinition { Name = name });
            SavePlaylists(_playlists);

            var playlist = _playlists.Last();
            var playlistsRoot = GetOrCreatePlaylistsRootNode();
            var playlistNode = BuildPlaylistNode(playlist);
            playlistsRoot.Children.Add(playlistNode);
            _collectionsTree.SelectedItem = playlistNode;
        }

        private void AddToPlaylistButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_selectedNode?.Routine == null)
            {
                AppendLog("Select a routine under Collections to add to a playlist.");
                return;
            }

            var targetPlaylist = _selectedNode?.Playlist
                ?? _playlists.FirstOrDefault()
                ?? CreateDefaultPlaylist();

            var routine = _selectedNode.Routine;
            var item = new PlaylistItemDefinition
            {
                Type = PlaylistItemType.Routine,
                DisplayName = routine.Name,
                RoutineId = GetRoutineKey(routine),
                ParameterValues = _parameterViewModels.ToDictionary(p => p.Name, p => p.Value, StringComparer.OrdinalIgnoreCase),
            };

            targetPlaylist.Items.Add(item);
            SavePlaylists(_playlists);

            var playlistsRoot = GetOrCreatePlaylistsRootNode();
            var playlistNode = playlistsRoot.Children.FirstOrDefault(n =>
                n.Kind == ScriptNodeKind.Playlist &&
                string.Equals(n.Name, targetPlaylist.Name, StringComparison.OrdinalIgnoreCase));

            if (playlistNode == null)
            {
                playlistNode = BuildPlaylistNode(targetPlaylist);
                playlistsRoot.Children.Add(playlistNode);
            }

            var playlistItemNode = BuildPlaylistItemNode(targetPlaylist, item);
            playlistNode.Children.Add(playlistItemNode);
            _collectionsTree.SelectedItem = playlistItemNode;

            AppendLog($"Added '{routine.Name}' to playlist '{targetPlaylist.Name}'.");
        }

        private ScriptNode GetOrCreatePlaylistsRootNode()
        {
            var playlistsRoot = _treeNodes.FirstOrDefault(n => n.Kind == ScriptNodeKind.PlaylistsRoot);
            if (playlistsRoot != null)
            {
                return playlistsRoot;
            }

            playlistsRoot = new ScriptNode { Name = "PlayLists", Kind = ScriptNodeKind.PlaylistsRoot };
            _treeNodes.Add(playlistsRoot);
            return playlistsRoot;
        }

        private PlaylistDefinition CreateDefaultPlaylist()
        {
            var playlist = new PlaylistDefinition { Name = "Default Playlist" };
            _playlists.Add(playlist);
            return playlist;
        }

        private static bool TryConvert(Type type, string value, out object? output)
        {
            if (type == typeof(string))
            {
                output = value;
                return true;
            }

            if (type == typeof(int) && int.TryParse(value, out var i))
            {
                output = i;
                return true;
            }

            if (type == typeof(bool) && bool.TryParse(value, out var b))
            {
                output = b;
                return true;
            }

            if (type == typeof(double) && double.TryParse(value, out var d))
            {
                output = d;
                return true;
            }

            if (type.IsEnum && Enum.TryParse(type, value, true, out var e))
            {
                output = e;
                return true;
            }

            output = null;
            return false;
        }

        private void ReloadButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            _runtime.ReloadScripts();
        }

        private void GenerateProjectButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var commonProjectPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "ScriptorCommon", "ScriptorCommon.csproj"));
            if (!File.Exists(commonProjectPath))
            {
                commonProjectPath = null;
            }

            var commonAssemblyPath = typeof(Scripts.Scriptor.Logger).Assembly.Location;
            var snapshot = _runtime.CurrentSnapshot ?? new ScriptRuntimeSnapshot(Array.Empty<ScriptCollectionDescriptor>(), Array.Empty<ScriptPackageDependency>());
            var result = ScriptProjectGenerator.EnsureScriptProject(_runtime.ScriptsRoot, snapshot.PackageDependencies, commonProjectPath, commonAssemblyPath);
            foreach (var message in result.Messages)
            {
                AppendLog(message);
            }
        }

        private void Runtime_CompilationFailed(object? sender, IReadOnlyList<ScriptCompilationDiagnostic> diagnostics)
        {
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var diagnostic in diagnostics)
                {
                    AppendLog($"{diagnostic.Severity} {diagnostic.Id}: {diagnostic.Message} [{diagnostic.FilePath}] ({diagnostic.Line}:{diagnostic.Column})");
                }
            });
        }

        private static string GetRoutineKey(ScriptRoutineDescriptor routine)
        {
            return (routine.Method.DeclaringType?.FullName ?? "<unknown>") + "." + routine.Method.Name;
        }

        private static Dictionary<string, Dictionary<string, string>> LoadDefaultsFile()
        {
            try
            {
                var path = GetDefaultsPath();
                if (!File.Exists(path))
                {
                    return new Dictionary<string, Dictionary<string, string>>();
                }

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json) ?? new Dictionary<string, Dictionary<string, string>>();
            }
            catch
            {
                return new Dictionary<string, Dictionary<string, string>>();
            }
        }

        private static void SaveDefaultsFile(Dictionary<string, Dictionary<string, string>> defaults)
        {
            var path = GetDefaultsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private static string GetDefaultsPath()
        {
            var scriptsRoot = ResolveScriptsRoot();
            return Path.Combine(scriptsRoot, ".scriptor", "defaults.json");
        }

        private static string ResolveScriptsRoot()
        {
            var outputScripts = Path.Combine(AppContext.BaseDirectory, "Scripts");

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "User_Defined_Scripts");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            return outputScripts;
        }

        private static string GetPlaylistsPath()
        {
            var scriptsRoot = ResolveScriptsRoot();
            return Path.Combine(scriptsRoot, ".scriptor", "playlists.json");
        }

        private static List<PlaylistDefinition> LoadPlaylists()
        {
            try
            {
                var path = GetPlaylistsPath();
                if (!File.Exists(path))
                {
                    return new List<PlaylistDefinition>();
                }

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<PlaylistDefinition>>(json) ?? new List<PlaylistDefinition>();
            }
            catch
            {
                return new List<PlaylistDefinition>();
            }
        }

        private static void SavePlaylists(List<PlaylistDefinition> playlists)
        {
            var path = GetPlaylistsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(playlists, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        private void Logger_EntryWritten(Logger.LogEntry entry)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (string.IsNullOrWhiteSpace(entry.ScopeKey))
                {
                    AppendLog($"{entry.Level}: {entry.Message}");
                    return;
                }

                if (entry.IsProgress)
                {
                    UpdateRunProgress(entry.ScopeKey!, entry.ProgressKey ?? "progress", entry.ProgressValue ?? 0, entry.Message);
                }
                else if (!string.IsNullOrWhiteSpace(entry.ProgressKey))
                {
                    AddProgressDetailMessage(entry.ScopeKey!, entry.ProgressKey!, entry.Message, entry.Level);
                }
                else
                {
                    AddRunMessage(entry.ScopeKey!, entry.Message, entry.Level);
                }
            });
        }

        private void ClearRunLog()
        {
            _runRowsByScope.Clear();
            _runLogRowsPanel.Children.Clear();
        }

        private void ScrollRunLogToBottom()
        {
            _runLogScrollViewer.Offset = new Avalonia.Vector(_runLogScrollViewer.Offset.X, _runLogScrollViewer.Extent.Height);
        }

        private void StatusSpinnerTimer_Tick(object? sender, EventArgs e)
        {
            if (_runRowsByScope.Count == 0)
            {
                return;
            }

            _spinnerFrameIndex = (_spinnerFrameIndex + 1) % SpinnerFrames.Length;
            var frame = SpinnerFrames[_spinnerFrameIndex];

            foreach (var row in _runRowsByScope.Values)
            {
                if (!row.IsRunning)
                {
                    continue;
                }

                row.StatusText.Text = frame;
                row.StatusText.Foreground = RunningStatusBrush;
                row.StatusBadge.Background = Brushes.Transparent;
            }
        }

        private RoutineRunRowUi StartRunRow(string scopeId, string scriptName, DateTimeOffset startedAt, bool isRunning = true)
        {
            if (_runRowsByScope.TryGetValue(scopeId, out var existing))
            {
                return existing;
            }

            var detailsPanel = new StackPanel { Spacing = 4 };
            detailsPanel.IsVisible = true;

            var toggleButton = new Button
            {
                Content = "▸",
                Width = 20,
                Height = 20,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Padding = new Avalonia.Thickness(0)
            };

            var nameText = new TextBlock
            {
                Text = scriptName,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };

            var statusText = new TextBlock
            {
                Text = isRunning ? SpinnerFrames[0] : "•",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = isRunning ? RunningStatusBrush : IdleStatusBrush,
                FontWeight = FontWeight.SemiBold,
                FontFamily = new FontFamily("Consolas")
            };
            var statusBadge = new Border
            {
                CornerRadius = new Avalonia.CornerRadius(10),
                Padding = new Avalonia.Thickness(6, 2),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Background = Brushes.Transparent,
                Child = statusText,
                MinWidth = 26
            };
            var timeText = new TextBlock { Text = startedAt.ToLocalTime().ToString("HH:mm:ss"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };

            toggleButton.Click += (_, _) =>
            {
                if (_runRowsByScope.TryGetValue(scopeId, out var runRow) && runRow.IsRunning)
                {
                    return;
                }

                detailsPanel.IsVisible = !detailsPanel.IsVisible;
                toggleButton.Content = detailsPanel.IsVisible ? "▾" : "▸";
            };

            var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("26,*,120,120") };
            Grid.SetColumn(toggleButton, 0);
            Grid.SetColumn(nameText, 1);
            Grid.SetColumn(statusBadge, 2);
            Grid.SetColumn(timeText, 3);
            headerGrid.Children.Add(toggleButton);
            headerGrid.Children.Add(nameText);
            headerGrid.Children.Add(statusBadge);
            headerGrid.Children.Add(timeText);

            var containerGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto") };
            Grid.SetRow(headerGrid, 0);
            Grid.SetRow(detailsPanel, 1);
            containerGrid.Children.Add(headerGrid);
            containerGrid.Children.Add(detailsPanel);

            var border = new Border { BorderThickness = new Avalonia.Thickness(1), BorderBrush = Avalonia.Media.Brushes.Gray, Padding = new Avalonia.Thickness(4), Child = containerGrid };
            border.BorderBrush = PanelBorderBrush;
            border.Background = PanelFillBrush;
            border.CornerRadius = new Avalonia.CornerRadius(6);
            _runLogRowsPanel.Children.Add(border);
            ScrollRunLogToBottom();

            var row = new RoutineRunRowUi(scopeId, scriptName, toggleButton, detailsPanel, statusText, statusBadge, timeText, startedAt)
            {
                IsRunning = isRunning,
            };
            row.ToggleButton.Content = row.DetailsPanel.IsVisible ? "▾" : "▸";
            _runRowsByScope[scopeId] = row;
            return row;
        }

        private void AddRunMessage(string scopeId, string message, Logger.LogLevel level = Logger.LogLevel.Event)
        {
            if (!_runRowsByScope.TryGetValue(scopeId, out var row))
            {
                row = StartRunRow(scopeId, scopeId, DateTimeOffset.Now);
            }

            var levelText = level == Logger.LogLevel.Error ? "ERROR" : level == Logger.LogLevel.Warning ? "WARN" : "INFO";
            var levelBrush = level == Logger.LogLevel.Error
                ? Brushes.Red
                : level == Logger.LogLevel.Warning
                    ? Brushes.Goldenrod
                    : Brushes.DodgerBlue;

            var entryGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,120,120"), Margin = new Avalonia.Thickness(0, 1, 0, 1) };
            var childMarker = new TextBlock
            {
                Text = "↳",
                Foreground = Brushes.Gray,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            };

            var statusPill = new Border
            {
                CornerRadius = new Avalonia.CornerRadius(4),
                BorderThickness = new Avalonia.Thickness(1),
                BorderBrush = PanelBorderBrush,
                Background = Brushes.Transparent,
                Padding = new Avalonia.Thickness(6, 2),
                Child = new TextBlock
                {
                    Text = levelText,
                    Foreground = levelBrush,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            };

            var line = new TextBlock
            {
                Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = level == Logger.LogLevel.Error
                    ? Avalonia.Media.Brushes.Red
                    : level == Logger.LogLevel.Warning
                        ? Avalonia.Media.Brushes.Goldenrod
                        : Avalonia.Media.Brushes.Gainsboro
            };

            var timeText = new TextBlock
            {
                Text = DateTime.Now.ToString("HH:mm:ss"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = Brushes.Gray,
                FontFamily = new FontFamily("Consolas")
            };

            var detailsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
            Grid.SetColumn(statusPill, 0);
            Grid.SetColumn(line, 1);
            detailsGrid.Children.Add(statusPill);
            detailsGrid.Children.Add(line);

            Grid.SetColumn(childMarker, 0);
            Grid.SetColumn(detailsGrid, 1);
            Grid.SetColumn(timeText, 3);
            entryGrid.Children.Add(childMarker);
            entryGrid.Children.Add(detailsGrid);
            entryGrid.Children.Add(timeText);

            var rowBorder = new Border
            {
                BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
                BorderBrush = PanelBorderBrush,
                Padding = new Avalonia.Thickness(0, 1, 0, 2),
                Child = entryGrid,
            };

            row.DetailsPanel.Children.Add(rowBorder);
            ScrollRunLogToBottom();
        }

        private ProgressRowUi EnsureProgressRow(string scopeId, string progressKey, string message)
        {
            if (!_runRowsByScope.TryGetValue(scopeId, out var row))
            {
                row = StartRunRow(scopeId, scopeId, DateTimeOffset.Now);
            }

            if (!row.ProgressBars.TryGetValue(progressKey, out var progressBar))
            {
                var startedAt = DateTimeOffset.Now;
                progressBar = new ProgressRowUi
                {
                    StartedAt = startedAt,
                    Progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 14 },
                    PercentageText = new TextBlock
                    {
                        Text = "0%",
                        FontFamily = new FontFamily("Consolas"),
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        Foreground = Brushes.Gainsboro,
                    },
                    MessageText = new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(message) ? progressKey : message,
                        TextWrapping = TextWrapping.NoWrap,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = Brushes.Gainsboro,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    }
                };
                row.ProgressBars[progressKey] = progressBar;

                var rowContainer = new StackPanel { Spacing = 2 };

                var entryGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,120,120"), Margin = new Avalonia.Thickness(0, 1, 0, 1) };
                var childMarker = new TextBlock
                {
                    Text = "↳",
                    Foreground = Brushes.Gray,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                };

                var taskPillText = new TextBlock
                {
                    Text = "TASK",
                    Foreground = Brushes.DeepSkyBlue,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                };

                var taskPill = new Button
                {
                    Background = Brushes.Transparent,
                    BorderBrush = PanelBorderBrush,
                    BorderThickness = new Avalonia.Thickness(1),
                    CornerRadius = new Avalonia.CornerRadius(4),
                    Padding = new Avalonia.Thickness(6, 1),
                    Content = taskPillText,
                    MinWidth = 58,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                };

                var progressStatus = new Grid { ColumnDefinitions = new ColumnDefinitions("*,40") };
                Grid.SetColumn(progressBar.Progress, 0);
                Grid.SetColumn(progressBar.PercentageText, 1);
                progressStatus.Children.Add(progressBar.Progress);
                progressStatus.Children.Add(progressBar.PercentageText);

                progressBar.ElapsedText = new TextBlock
                {
                    Text = "00:00.000",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                    FontFamily = new FontFamily("Consolas")
                };

                progressBar.DetailsPanel = new StackPanel { Spacing = 1, IsVisible = false, Margin = new Avalonia.Thickness(20, 0, 0, 0) };

                taskPill.Click += (_, _) =>
                {
                    progressBar.DetailsPanel.IsVisible = !progressBar.DetailsPanel.IsVisible;
                    taskPillText.Text = progressBar.DetailsPanel.IsVisible ? "TASK ▾" : "TASK ▸";
                };

                var detailsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
                Grid.SetColumn(taskPill, 0);
                Grid.SetColumn(progressBar.MessageText, 1);
                detailsGrid.Children.Add(taskPill);
                detailsGrid.Children.Add(progressBar.MessageText);

                Grid.SetColumn(childMarker, 0);
                Grid.SetColumn(detailsGrid, 1);
                Grid.SetColumn(progressStatus, 2);
                Grid.SetColumn(progressBar.ElapsedText, 3);
                entryGrid.Children.Add(childMarker);
                entryGrid.Children.Add(detailsGrid);
                entryGrid.Children.Add(progressStatus);
                entryGrid.Children.Add(progressBar.ElapsedText);

                var rowBorder = new Border
                {
                    BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
                    BorderBrush = PanelBorderBrush,
                    Padding = new Avalonia.Thickness(0, 1, 0, 2),
                    Child = entryGrid,
                };

                rowContainer.Children.Add(rowBorder);
                rowContainer.Children.Add(progressBar.DetailsPanel);
                row.DetailsPanel.Children.Add(rowContainer);
            }

            return progressBar;
        }

        private void UpdateRunProgress(string scopeId, string progressKey, double value, string message)
        {
            var progressBar = EnsureProgressRow(scopeId, progressKey, message);

            var clamped = Math.Max(0, Math.Min(100, value));
            progressBar.Progress.Value = clamped;
            progressBar.PercentageText.Text = $"{clamped:0.#}%";
            var elapsed = DateTimeOffset.Now - progressBar.StartedAt;
            progressBar.ElapsedText.Text = elapsed.ToString(@"mm\:ss\.fff");
            if (!string.IsNullOrWhiteSpace(message))
            {
                progressBar.MessageText.Text = $"[{DateTime.Now:HH:mm:ss}] {message}";
            }

            if (progressBar.LastDetailPercent is null || Math.Abs(clamped - progressBar.LastDetailPercent.Value) >= 10)
            {
                progressBar.DetailsPanel.Children.Add(new TextBlock
                {
                    Text = $"[{DateTime.Now:HH:mm:ss}] {clamped:0.#}% - {message}",
                    Foreground = Brushes.Gray,
                    FontFamily = new FontFamily("Consolas")
                });
                progressBar.LastDetailPercent = clamped;
            }

            ScrollRunLogToBottom();
        }

        private void AddProgressDetailMessage(string scopeId, string progressKey, string message, Logger.LogLevel level)
        {
            var progressRow = EnsureProgressRow(scopeId, progressKey, progressKey);

            var detail = new TextBlock
            {
                Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = level == Logger.LogLevel.Error
                    ? Brushes.Red
                    : level == Logger.LogLevel.Warning
                        ? Brushes.Goldenrod
                        : Brushes.Gainsboro
            };

            progressRow.DetailsPanel.Children.Add(detail);
            ScrollRunLogToBottom();
        }

        private void CompleteRunRow(string scopeId, bool success, TimeSpan elapsed, DateTimeOffset startedAt)
        {
            if (!_runRowsByScope.TryGetValue(scopeId, out var row))
            {
                return;
            }

            row.IsRunning = false;
            row.StatusText.Text = success ? "✓" : "✗";
            row.StatusText.Foreground = success ? SuccessStatusBrush : FailureStatusBrush;
            row.StatusBadge.Background = Brushes.Transparent;
            row.TimeText.Text = elapsed.ToString(@"mm\:ss\.fff");
        }

        private void AppendLog(string message)
        {
            var row = StartRunRow("system", "System", DateTimeOffset.Now, false);
            var line = new TextBlock { Text = $"[{DateTime.Now:HH:mm:ss}] {message}", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            row.DetailsPanel.Children.Add(line);
            row.DetailsPanel.IsVisible = true;
            row.ToggleButton.Content = "▾";
        }

        private sealed class RoutineRunRowUi
        {
            public RoutineRunRowUi(
                string scopeId,
                string scriptName,
                Button toggleButton,
                StackPanel detailsPanel,
                TextBlock statusText,
                Border statusBadge,
                TextBlock timeText,
                DateTimeOffset startedAt)
            {
                ScopeId = scopeId;
                ScriptName = scriptName;
                ToggleButton = toggleButton;
                DetailsPanel = detailsPanel;
                StatusText = statusText;
                StatusBadge = statusBadge;
                TimeText = timeText;
                StartedAt = startedAt;
            }

            public string ScopeId { get; }
            public string ScriptName { get; }
            public Button ToggleButton { get; }
            public StackPanel DetailsPanel { get; }
            public TextBlock StatusText { get; }
            public Border StatusBadge { get; }
            public TextBlock TimeText { get; }
            public DateTimeOffset StartedAt { get; }
            public Dictionary<string, ProgressRowUi> ProgressBars { get; } = new(StringComparer.OrdinalIgnoreCase);
            public bool IsRunning { get; set; }
        }

        private sealed class ProgressRowUi
        {
            public DateTimeOffset StartedAt { get; set; }
            public ProgressBar Progress { get; set; } = null!;
            public TextBlock PercentageText { get; set; } = null!;
            public TextBlock MessageText { get; set; } = null!;
            public TextBlock ElapsedText { get; set; } = null!;
            public StackPanel DetailsPanel { get; set; } = null!;
            public double? LastDetailPercent { get; set; }
        }

        protected override void OnClosed(EventArgs e)
        {
            Logger.EntryWritten -= Logger_EntryWritten;
            _statusSpinnerTimer.Stop();
            _statusSpinnerTimer.Tick -= StatusSpinnerTimer_Tick;
            _runtime.Dispose();
            base.OnClosed(e);
        }
    }
}

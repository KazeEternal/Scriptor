using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Scripts.Scriptor;
using Scripts.Scriptor.Conductor;

namespace GUI
{
    public sealed partial class MainWindow : Window
    {
        private readonly ScriptRuntimeService _runtime;
        private readonly List<ParameterViewModel> _parameterViewModels = new();

        private StackPanel _collectionsPanel = null!;
        private TextBlock _collectionDescriptionBox = null!;
        private StackPanel _parameterPanel = null!;
        private TextBlock _routineDescriptionBox = null!;
        private Button _reloadButton = null!;
        private Button _runButton = null!;
        private Button _generateProjectButton = null!;
        private Button _saveDefaultsButton = null!;
        private Button _runSelectedButton = null!;
        private TextBlock _scriptPathStatus = null!;
        private TextBox _logBox = null!;

        private ScriptRoutineDescriptor? _currentRoutine;

        public MainWindow()
        {
            InitializeComponent();

            _reloadButton.Click += ReloadButton_Click;
            _runButton.Click += RunButton_Click;
            _generateProjectButton.Click += GenerateProjectButton_Click;
            _saveDefaultsButton.Click += (_, _) => SaveCurrentRoutineDefaults();
            _runSelectedButton.Click += RunButton_Click;

            var scriptsRoot = ResolveScriptsRoot();
            Title = $"Scriptor GUI - {scriptsRoot}";
            _scriptPathStatus.Text = $"Scripts path: {scriptsRoot}";
            _runtime = new ScriptRuntimeService(scriptsRoot);
            _runtime.ScriptsReloaded += Runtime_ScriptsReloaded;
            _runtime.CompilationFailed += Runtime_CompilationFailed;

            Logger.Event += (f, a) => Dispatcher.UIThread.Post(() => AppendLog(string.Format(f, a)));
            Logger.Warning += (f, a) => Dispatcher.UIThread.Post(() => AppendLog("WARN: " + string.Format(f, a)));
            Logger.Error += (f, a) => Dispatcher.UIThread.Post(() => AppendLog("ERR: " + string.Format(f, a)));

            _runtime.StartWatching();
            _runtime.ReloadScripts();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _collectionsPanel = this.FindControl<StackPanel>("CollectionsPanel") ?? throw new InvalidOperationException("CollectionsPanel not found.");
            _collectionDescriptionBox = this.FindControl<TextBlock>("CollectionDescription") ?? throw new InvalidOperationException("CollectionDescription not found.");
            _parameterPanel = this.FindControl<StackPanel>("ParameterPanel") ?? throw new InvalidOperationException("ParameterPanel not found.");
            _routineDescriptionBox = this.FindControl<TextBlock>("RoutineDescription") ?? throw new InvalidOperationException("RoutineDescription not found.");
            _reloadButton = this.FindControl<Button>("ReloadButton") ?? throw new InvalidOperationException("ReloadButton not found.");
            _runButton = this.FindControl<Button>("RunButton") ?? throw new InvalidOperationException("RunButton not found.");
            _generateProjectButton = this.FindControl<Button>("GenerateProjectButton") ?? throw new InvalidOperationException("GenerateProjectButton not found.");
            _saveDefaultsButton = this.FindControl<Button>("SaveDefaultsButton") ?? throw new InvalidOperationException("SaveDefaultsButton not found.");
            _runSelectedButton = this.FindControl<Button>("RunSelectedButton") ?? throw new InvalidOperationException("RunSelectedButton not found.");
            _scriptPathStatus = this.FindControl<TextBlock>("ScriptPathStatus") ?? throw new InvalidOperationException("ScriptPathStatus not found.");
            _logBox = this.FindControl<TextBox>("LogBox") ?? throw new InvalidOperationException("LogBox not found.");
        }

        private void Runtime_ScriptsReloaded(object? sender, ScriptRuntimeSnapshot snapshot)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _collectionsPanel.Children.Clear();
                _collectionDescriptionBox.Text = string.Empty;

                foreach (var collection in snapshot.Collections)
                {
                    _collectionsPanel.Children.Add(CreateCollectionCard(collection));
                }

                AppendLog($"Loaded {snapshot.Collections.Count} collections.");
            });
        }

        private Control CreateCollectionCard(ScriptCollectionDescriptor collection)
        {
            var panel = new StackPanel { Spacing = 6, Margin = new Avalonia.Thickness(0, 4, 0, 4) };

            foreach (var routine in collection.Routines)
            {
                var button = new Button
                {
                    Content = routine.Name,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left
                };
                button.Click += (_, _) =>
                {
                    _collectionDescriptionBox.Text = collection.Description ?? string.Empty;
                    DisplayRoutine(routine);
                };
                panel.Children.Add(button);
            }

            var expander = new Expander
            {
                Header = $"{collection.Name} ({collection.Routines.Count} scripts)",
                Content = panel,
                IsExpanded = false,
                Margin = new Avalonia.Thickness(0, 0, 0, 8)
            };

            expander.Expanded += (_, _) =>
            {
                _collectionDescriptionBox.Text = collection.Description ?? string.Empty;
            };

            return expander;
        }

        private void DisplayRoutine(ScriptRoutineDescriptor routine)
        {
            SaveCurrentRoutineDefaults();

            _currentRoutine = routine;
            _routineDescriptionBox.Text = routine.Description ?? string.Empty;
            _parameterViewModels.Clear();
            _parameterPanel.Children.Clear();

            var defaults = LoadDefaultsFile();
            defaults.TryGetValue(GetRoutineKey(routine), out var saved);

            foreach (var parameter in routine.Parameters)
            {
                var name = parameter.DisplayName ?? parameter.Name;
                var value = parameter.DefaultValue?.ToString() ?? string.Empty;
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
            if (_currentRoutine == null)
            {
                return;
            }

            var defaults = LoadDefaultsFile();
            defaults[GetRoutineKey(_currentRoutine)] = _parameterViewModels.ToDictionary(p => p.Name, p => p.Value);
            SaveDefaultsFile(defaults);
        }

        private async void RunButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
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
            AppendLog($"Running {_currentRoutine.Name}...");
            var result = await _runtime.ExecuteRoutineAsync(_currentRoutine, converted).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                AppendLog(result.IsSuccess ? $"{_currentRoutine.Name} completed successfully." : $"{_currentRoutine.Name} failed.");
                if (result.Exception != null)
                {
                    AppendLog(result.Exception.ToString());
                }
            });
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

        private void AppendLog(string message)
        {
            _logBox.Text = string.IsNullOrWhiteSpace(_logBox.Text)
                ? message
                : _logBox.Text + Environment.NewLine + message;
        }

        protected override void OnClosed(EventArgs e)
        {
            _runtime.Dispose();
            base.OnClosed(e);
        }
    }
}

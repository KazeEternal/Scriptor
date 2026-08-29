using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.ViewModel;
using Scripts.Scriptor.Conductor;

namespace GUI
{
    internal sealed class QuickCommandWindow : Window
    {
        private readonly IReadOnlyList<ScriptRoutineDescriptor> _routines;
        private readonly IReadOnlyList<CommandDefinition> _commands;
        private readonly Func<ScriptRoutineDescriptor, IReadOnlyDictionary<string, string>, Task<string?>> _runRoutine;
        private readonly Func<CommandDefinition, Task<string?>> _runDefinedCommand;
        private readonly Action<QuickCommandAction> _runBuiltInCommand;
        private readonly TextBox _input = new();
        private readonly ListBox _suggestions = new();
        private readonly TextBlock _details = new();
        private readonly TextBlock _status = new();
        private readonly List<QuickCommandSuggestion> _matches = new();

        public QuickCommandWindow(
            IReadOnlyList<ScriptRoutineDescriptor> routines,
            IReadOnlyList<CommandDefinition> commands,
            Func<ScriptRoutineDescriptor, IReadOnlyDictionary<string, string>, Task<string?>> runRoutine,
            Func<CommandDefinition, Task<string?>> runDefinedCommand,
            Action<QuickCommandAction> runBuiltInCommand)
        {
            _routines = routines;
            _commands = commands;
            _runRoutine = runRoutine;
            _runDefinedCommand = runDefinedCommand;
            _runBuiltInCommand = runBuiltInCommand;

            Title = "Scriptor Quick Command";
            Width = 720;
            Height = 340;
            MinWidth = 520;
            MinHeight = 240;
            Topmost = true;
            ShowInTaskbar = false;

            var root = new Grid
            {
                Margin = new Thickness(14),
                RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"),
            };

            _input.Watermark = "Search scripts or enter >reload, >show, or >minimize";
            _input.Margin = new Thickness(0, 0, 0, 8);
            _input.TextChanged += (_, _) => RefreshSuggestions();
            _input.KeyDown += Input_KeyDown;
            root.Children.Add(_input);

            _suggestions.ItemTemplate = new FuncDataTemplate<QuickCommandSuggestion>((suggestion, _) =>
                new TextBlock { Text = suggestion?.Label ?? string.Empty, Margin = new Thickness(6) });
            _suggestions.DoubleTapped += async (_, _) => await ExecuteAsync();
            _suggestions.SelectionChanged += (_, _) => UpdateSelectedDetails();
            Grid.SetRow(_suggestions, 1);
            root.Children.Add(_suggestions);

            _details.Foreground = Brushes.LightGray;
            _details.TextWrapping = TextWrapping.Wrap;
            _details.MaxHeight = 78;
            Grid.SetRow(_details, 2);
            root.Children.Add(_details);

            _status.Foreground = Brushes.Gray;
            _status.Margin = new Thickness(0, 8, 0, 0);
            _status.Text = "Enter runs the selected suggestion. Script parameter overrides use: Script Name -- Parameter=Value; Parameter=Value";
            _status.TextWrapping = TextWrapping.Wrap;
            Grid.SetRow(_status, 3);
            root.Children.Add(_status);

            Content = root;
            Opened += (_, _) =>
            {
                _input.Focus();
                RefreshSuggestions();
            };
        }

        private void RefreshSuggestions()
        {
            var query = _input.Text?.Trim() ?? string.Empty;
            _matches.Clear();

            if (query.StartsWith('>'))
            {
                foreach (var command in QuickCommandSuggestion.Commands)
                {
                    if (Matches(query, command.Label, command.Input))
                    {
                        _matches.Add(command);
                    }
                }
            }
            else
            {
                foreach (var command in _commands)
                {
                    var suggestion = QuickCommandSuggestion.ForDefinedCommand(command);
                    if (Matches(query, suggestion.Label, suggestion.Input))
                    {
                        _matches.Add(suggestion);
                    }
                }

                foreach (var routine in _routines)
                {
                    var suggestion = QuickCommandSuggestion.ForRoutine(routine);
                    if (Matches(query, suggestion.Label, suggestion.Input))
                    {
                        _matches.Add(suggestion);
                    }
                }
            }

            _suggestions.ItemsSource = null;
            _suggestions.ItemsSource = _matches.Take(20).ToList();
            _suggestions.SelectedIndex = _matches.Count > 0 ? 0 : -1;
            UpdateSelectedDetails();
        }

        private void UpdateSelectedDetails()
        {
            if (_suggestions.SelectedItem is not QuickCommandSuggestion suggestion)
            {
                _details.Text = string.Empty;
                return;
            }

            if (suggestion.Command != null)
            {
                _details.Text = suggestion.Command.Description;
                return;
            }

            if (suggestion.Routine is not { } routine)
            {
                _details.Text = string.Empty;
                return;
            }

            var parameterDetails = routine.Parameters.Select(parameter =>
            {
                var name = parameter.DisplayName ?? parameter.Name;
                var description = string.IsNullOrWhiteSpace(parameter.Description)
                    ? parameter.ParameterType.Name
                    : parameter.Description;
                var defaultValue = parameter.DefaultValue?.ToString() ?? "(empty)";
                return $"{name}: {description} Default: {defaultValue}";
            });
            _details.Text = string.IsNullOrWhiteSpace(routine.Description)
                ? string.Join(Environment.NewLine, parameterDetails)
                : $"{routine.Description}{(routine.Parameters.Count > 0 ? Environment.NewLine : string.Empty)}{string.Join(Environment.NewLine, parameterDetails)}";
        }

        private static bool Matches(string query, string label, string input)
        {
            return string.IsNullOrWhiteSpace(query)
                || label.Contains(query, StringComparison.OrdinalIgnoreCase)
                || input.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private async void Input_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && _suggestions.SelectedIndex < _matches.Count - 1)
            {
                _suggestions.SelectedIndex++;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up && _suggestions.SelectedIndex > 0)
            {
                _suggestions.SelectedIndex--;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                await ExecuteAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab && _suggestions.SelectedItem is QuickCommandSuggestion suggestion)
            {
                _input.Text = suggestion.Input;
                var parameterStart = suggestion.Input.IndexOf(" -- ", StringComparison.Ordinal);
                if (parameterStart >= 0)
                {
                    _input.SelectionStart = parameterStart + 4;
                    _input.SelectionEnd = suggestion.Input.Length;
                }
                else
                {
                    _input.CaretIndex = suggestion.Input.Length;
                }

                e.Handled = true;
            }
        }

        private async Task ExecuteAsync()
        {
            var input = _input.Text?.Trim() ?? string.Empty;
            if (_suggestions.SelectedItem is QuickCommandSuggestion selected
                && !string.Equals(input, selected.Input, StringComparison.OrdinalIgnoreCase))
            {
                input = selected.Input;
            }

            if (QuickCommandSuggestion.TryGetCommand(input, out var command))
            {
                _runBuiltInCommand(command);
                Close();
                return;
            }

            if (_suggestions.SelectedItem is QuickCommandSuggestion { Command: { } definedCommand })
            {
                _status.Text = $"Running {definedCommand.Name}...";
                _status.Foreground = Brushes.Gray;
                var commandError = await _runDefinedCommand(definedCommand);
                if (commandError == null)
                {
                    Close();
                    return;
                }

                _status.Text = commandError;
                _status.Foreground = Brushes.IndianRed;
                return;
            }

            if (!TryParseRoutine(input, out var routine, out var values, out var error))
            {
                _status.Text = error;
                _status.Foreground = Brushes.IndianRed;
                return;
            }

            _status.Text = $"Running {routine.Name}...";
            _status.Foreground = Brushes.Gray;
            var runError = await _runRoutine(routine, values);
            if (runError == null)
            {
                Close();
                return;
            }

            _status.Text = runError;
            _status.Foreground = Brushes.IndianRed;
        }

        private bool TryParseRoutine(
            string input,
            out ScriptRoutineDescriptor routine,
            out IReadOnlyDictionary<string, string> values,
            out string error)
        {
            var segments = input.Split("--", 2, StringSplitOptions.TrimEntries);
            var routineName = segments[0];
            routine = _routines.FirstOrDefault(candidate => string.Equals(candidate.Name, routineName, StringComparison.OrdinalIgnoreCase))!;
            if (routine == null)
            {
                values = new Dictionary<string, string>();
                error = $"No routine named '{routineName}' was found.";
                return false;
            }

            var parsed = routine.Parameters.ToDictionary(
                parameter => parameter.DisplayName ?? parameter.Name,
                parameter => parameter.DefaultValue?.ToString() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
            if (segments.Length == 2 && !string.IsNullOrWhiteSpace(segments[1]))
            {
                foreach (var assignment in segments[1].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var pair = assignment.Split('=', 2, StringSplitOptions.TrimEntries);
                    if (pair.Length != 2 || !parsed.ContainsKey(pair[0]))
                    {
                        values = new Dictionary<string, string>();
                        error = $"Invalid parameter override '{assignment}'.";
                        return false;
                    }

                    parsed[pair[0]] = pair[1];
                }
            }

            values = parsed;
            error = string.Empty;
            return true;
        }

        private sealed class QuickCommandSuggestion
        {
            private QuickCommandSuggestion(
                string label,
                string input,
                ScriptRoutineDescriptor? routine = null,
                CommandDefinition? command = null)
            {
                Label = label;
                Input = input;
                Routine = routine;
                Command = command;
            }

            public string Label { get; }
            public string Input { get; }
            public ScriptRoutineDescriptor? Routine { get; }
            public CommandDefinition? Command { get; }

            public static IReadOnlyList<QuickCommandSuggestion> Commands { get; } =
            [
                new("Command: Reload scripts", ">reload"),
                new("Command: Show Scriptor", ">show"),
                new("Command: Minimize Scriptor", ">minimize"),
            ];

            public static QuickCommandSuggestion ForRoutine(ScriptRoutineDescriptor routine)
            {
                var values = routine.Parameters
                    .Select(parameter => $"{parameter.DisplayName ?? parameter.Name}={parameter.DefaultValue}")
                    .ToList();
                var input = values.Count == 0
                    ? routine.Name
                    : $"{routine.Name} -- {string.Join("; ", values)}";
                var description = string.IsNullOrWhiteSpace(routine.Description)
                    ? string.Empty
                    : $" — {routine.Description}";
                return new QuickCommandSuggestion($"Run: {routine.Name}{description}", input, routine);
            }

            public static QuickCommandSuggestion ForDefinedCommand(CommandDefinition command)
            {
                var description = string.IsNullOrWhiteSpace(command.Description)
                    ? string.Empty
                    : $" — {command.Description}";
                return new QuickCommandSuggestion($"Command: {command.Name}{description}", command.Name, command: command);
            }

            public static bool TryGetCommand(string input, out QuickCommandAction command)
            {
                command = input.ToLowerInvariant() switch
                {
                    ">reload" => QuickCommandAction.Reload,
                    ">show" => QuickCommandAction.Show,
                    ">minimize" => QuickCommandAction.Minimize,
                    _ => default,
                };
                return input.Equals(">reload", StringComparison.OrdinalIgnoreCase)
                    || input.Equals(">show", StringComparison.OrdinalIgnoreCase)
                    || input.Equals(">minimize", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    internal enum QuickCommandAction
    {
        Reload,
        Show,
        Minimize,
    }
}

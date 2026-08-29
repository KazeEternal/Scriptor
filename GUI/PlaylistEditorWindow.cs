using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using GUI.ViewModel;
using Scripts.Scriptor.Conductor;

namespace GUI
{
    public sealed class PlaylistEditorWindow : Window
    {
        private readonly List<PlaylistDefinition> _playlists;
        private readonly IReadOnlyList<ScriptRoutineDescriptor> _routines;
        private readonly Func<ScriptRoutineDescriptor, string> _getRoutineKey;
        private readonly Action<PlaylistDefinition?> _save;
        private readonly ComboBox _playlistSelector = new();
        private readonly ComboBox _routineSelector = new();
        private readonly ListBox _itemsList = new();
        private readonly TextBlock _emptyState = new();
        private readonly List<PlaylistEditorEntry> _entries = new();
        private PlaylistDefinition? _selectedPlaylist;
        private PlaylistEditorEntry? _selectedEntry;

        public PlaylistEditorWindow(
            List<PlaylistDefinition> playlists,
            IReadOnlyList<ScriptRoutineDescriptor> routines,
            Func<ScriptRoutineDescriptor, string> getRoutineKey,
            Action<PlaylistDefinition?> save,
            PlaylistDefinition? selectedPlaylist = null,
            ScriptRoutineDescriptor? routineToAdd = null)
        {
            _playlists = playlists;
            _routines = routines;
            _getRoutineKey = getRoutineKey;
            _save = save;
            _selectedPlaylist = selectedPlaylist;

            Title = "Edit Playlists";
            Width = 760;
            Height = 620;
            MinWidth = 620;
            MinHeight = 460;

            var root = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"),
            };

            var playlistActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _playlistSelector.Width = 280;
            _playlistSelector.SelectionChanged += (_, _) => SelectPlaylist(_playlistSelector.SelectedItem as PlaylistDefinition);
            playlistActions.Children.Add(_playlistSelector);
            playlistActions.Children.Add(CreateButton("New", (_, _) => CreatePlaylist()));
            playlistActions.Children.Add(CreateButton("Rename", async (_, _) => await RenamePlaylistAsync()));
            playlistActions.Children.Add(CreateButton("Delete", async (_, _) => await DeletePlaylistAsync()));
            root.Children.Add(playlistActions);

            var itemActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _routineSelector.Width = 280;
            _routineSelector.ItemTemplate = new FuncDataTemplate<ScriptRoutineDescriptor>((routine, _) =>
                new TextBlock { Text = routine.Name });
            _routineSelector.ItemsSource = _routines;
            itemActions.Children.Add(_routineSelector);
            itemActions.Children.Add(CreateButton("Add Routine", (_, _) => AddRoutine()));
            itemActions.Children.Add(CreateButton("Add Parallel Group", (_, _) => AddParallelGroup()));
            itemActions.Children.Add(CreateButton("Remove", (_, _) => RemoveSelectedItem()));
            itemActions.Children.Add(CreateButton("Move Up", (_, _) => MoveSelectedItem(-1)));
            itemActions.Children.Add(CreateButton("Move Down", (_, _) => MoveSelectedItem(1)));
            itemActions.Margin = new Thickness(0, 10, 0, 10);
            Grid.SetRow(itemActions, 1);
            root.Children.Add(itemActions);

            _itemsList.SelectionChanged += (_, _) => _selectedEntry = _itemsList.SelectedItem as PlaylistEditorEntry;
            _itemsList.ItemTemplate = new FuncDataTemplate<PlaylistEditorEntry>((entry, _) =>
                new TextBlock
                {
                    Text = entry.Label,
                    Margin = new Thickness(entry.Depth * 22, 6, 6, 6),
                });

            _emptyState.Text = "Create a playlist, then add routines or a parallel group.";
            _emptyState.Foreground = Brushes.Gray;
            _emptyState.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyState.VerticalAlignment = VerticalAlignment.Center;
            var itemHost = new Grid();
            itemHost.Children.Add(_itemsList);
            itemHost.Children.Add(_emptyState);
            Grid.SetRow(itemHost, 2);
            root.Children.Add(itemHost);

            var help = new TextBlock
            {
                Text = "Routines run in listed order. Routines inside a parallel group start together. Select a group before adding a routine to it.",
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
            };
            Grid.SetRow(help, 3);
            root.Children.Add(help);

            Content = root;
            RefreshPlaylists();
            _routineSelector.SelectedItem = routineToAdd;
        }

        public string? SelectedPlaylistName => _selectedPlaylist?.Name;
        public string? SelectedPlaylistItemId => _selectedEntry?.Item?.Id;

        private static Button CreateButton(string content, EventHandler<Avalonia.Interactivity.RoutedEventArgs> onClick)
        {
            var button = new Button { Content = content };
            button.Click += onClick;
            return button;
        }

        private void RefreshPlaylists(PlaylistDefinition? selectPlaylist = null, string? selectItemId = null)
        {
            _playlistSelector.ItemsSource = null;
            _playlistSelector.ItemsSource = _playlists;
            _playlistSelector.ItemTemplate = new FuncDataTemplate<PlaylistDefinition>((playlist, _) =>
                new TextBlock { Text = playlist.Name });

            var playlist = selectPlaylist ?? _selectedPlaylist ?? _playlists.FirstOrDefault();
            _playlistSelector.SelectedItem = playlist;
            SelectPlaylist(playlist, selectItemId);
        }

        private void SelectPlaylist(PlaylistDefinition? playlist, string? selectItemId = null)
        {
            _selectedPlaylist = playlist;
            _entries.Clear();

            if (playlist != null)
            {
                foreach (var item in playlist.Items)
                {
                    AddEntry(item, null, 0);
                }
            }

            _itemsList.ItemsSource = null;
            _itemsList.ItemsSource = _entries;
            _selectedEntry = _entries.FirstOrDefault(entry => entry.Item.Id == selectItemId) ?? _entries.FirstOrDefault();
            _itemsList.SelectedItem = _selectedEntry;
            _emptyState.IsVisible = playlist == null || _entries.Count == 0;
        }

        private void AddEntry(PlaylistItemDefinition item, PlaylistItemDefinition? parentGroup, int depth)
        {
            _entries.Add(new PlaylistEditorEntry(item, parentGroup, depth));
            if (item.Type != PlaylistItemType.ParallelGroup)
            {
                return;
            }

            foreach (var child in item.Children)
            {
                AddEntry(child, item, depth + 1);
            }
        }

        private void CreatePlaylist()
        {
            var name = GetUniquePlaylistName("New Playlist");
            var playlist = new PlaylistDefinition { Name = name };
            _playlists.Add(playlist);
            PersistAndRefresh(playlist);
        }

        private async System.Threading.Tasks.Task RenamePlaylistAsync()
        {
            if (_selectedPlaylist == null)
            {
                return;
            }

            var name = await PlaylistNameDialog.ShowAsync(this, "Rename Playlist", _selectedPlaylist.Name);
            if (name == null || string.Equals(name, _selectedPlaylist.Name, StringComparison.Ordinal))
            {
                return;
            }

            if (_playlists.Any(playlist => !ReferenceEquals(playlist, _selectedPlaylist)
                && string.Equals(playlist.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                await PlaylistNameDialog.ShowMessageAsync(this, "Playlist names must be unique.");
                return;
            }

            _selectedPlaylist.Name = name;
            PersistAndRefresh(_selectedPlaylist);
        }

        private async System.Threading.Tasks.Task DeletePlaylistAsync()
        {
            if (_selectedPlaylist == null)
            {
                return;
            }

            var delete = await PlaylistNameDialog.ConfirmAsync(this, $"Delete playlist '{_selectedPlaylist.Name}'?");
            if (!delete)
            {
                return;
            }

            _playlists.Remove(_selectedPlaylist);
            PersistAndRefresh();
        }

        private void AddRoutine()
        {
            if (_selectedPlaylist == null || _routineSelector.SelectedItem is not ScriptRoutineDescriptor routine)
            {
                return;
            }

            var item = new PlaylistItemDefinition
            {
                Type = PlaylistItemType.Routine,
                DisplayName = routine.Name,
                RoutineId = _getRoutineKey(routine),
                ParameterValues = routine.Parameters.ToDictionary(
                    parameter => parameter.DisplayName ?? parameter.Name,
                    parameter => parameter.DefaultValue?.ToString() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            };

            if (_selectedEntry?.Item.Type == PlaylistItemType.ParallelGroup)
            {
                _selectedEntry.Item.Children.Add(item);
            }
            else
            {
                _selectedPlaylist.Items.Add(item);
            }

            PersistAndRefresh(_selectedPlaylist, item.Id);
        }

        private void AddParallelGroup()
        {
            if (_selectedPlaylist == null)
            {
                return;
            }

            var group = new PlaylistItemDefinition
            {
                Type = PlaylistItemType.ParallelGroup,
                DisplayName = GetUniqueGroupName(_selectedPlaylist, "Parallel Routines"),
            };
            _selectedPlaylist.Items.Add(group);
            PersistAndRefresh(_selectedPlaylist, group.Id);
        }

        private void RemoveSelectedItem()
        {
            if (_selectedPlaylist == null || _selectedEntry == null)
            {
                return;
            }

            GetContainer(_selectedPlaylist, _selectedEntry.ParentGroup).Remove(_selectedEntry.Item);
            PersistAndRefresh(_selectedPlaylist);
        }

        private void MoveSelectedItem(int offset)
        {
            if (_selectedPlaylist == null || _selectedEntry == null)
            {
                return;
            }

            var container = GetContainer(_selectedPlaylist, _selectedEntry.ParentGroup);
            var index = container.IndexOf(_selectedEntry.Item);
            var targetIndex = index + offset;
            if (targetIndex < 0 || targetIndex >= container.Count)
            {
                return;
            }

            (container[index], container[targetIndex]) = (container[targetIndex], container[index]);
            PersistAndRefresh(_selectedPlaylist, _selectedEntry.Item.Id);
        }

        private void PersistAndRefresh(PlaylistDefinition? selectPlaylist = null, string? selectItemId = null)
        {
            _save(_selectedPlaylist);
            RefreshPlaylists(selectPlaylist, selectItemId);
        }

        private static List<PlaylistItemDefinition> GetContainer(PlaylistDefinition playlist, PlaylistItemDefinition? parentGroup)
        {
            return parentGroup?.Children ?? playlist.Items;
        }

        private string GetUniquePlaylistName(string baseName)
        {
            var name = baseName;
            var suffix = 2;
            while (_playlists.Any(playlist => string.Equals(playlist.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} {suffix++}";
            }

            return name;
        }

        private static string GetUniqueGroupName(PlaylistDefinition playlist, string baseName)
        {
            var names = playlist.Items
                .Where(item => item.Type == PlaylistItemType.ParallelGroup)
                .Select(item => item.DisplayName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = baseName;
            var suffix = 2;
            while (names.Contains(name))
            {
                name = $"{baseName} {suffix++}";
            }

            return name;
        }

        private sealed class PlaylistEditorEntry
        {
            public PlaylistEditorEntry(PlaylistItemDefinition item, PlaylistItemDefinition? parentGroup, int depth)
            {
                Item = item;
                ParentGroup = parentGroup;
                Depth = depth;
                Label = item.Type == PlaylistItemType.ParallelGroup
                    ? $"Parallel: {item.DisplayName}"
                    : item.DisplayName;
            }

            public PlaylistItemDefinition Item { get; }
            public PlaylistItemDefinition? ParentGroup { get; }
            public int Depth { get; }
            public string Label { get; }
        }
    }
}
